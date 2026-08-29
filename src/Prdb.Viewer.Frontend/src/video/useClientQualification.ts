import { useEffect } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  api,
  type Account,
  type ClientPlaybackAssessmentReport,
  type UnassessedPlaybackProfile,
} from '../api/client'
import { queryKeys } from '../queryKeys'

/// Qualifies this browser against the media configurations the library actually holds.
///
/// Client Video Playability is per Account and per client, and the only one who can answer for a
/// client is the client. This asks about configurations it has not answered for — including those
/// of Videos it currently cannot see, which is exactly the set an unqualified client is missing —
/// measures each with Media Capabilities where the inspected facts determine a full codec string,
/// falls back to the coarser support test where they do not, and reports what it found.
///
/// It runs in the shell rather than on a screen: what this browser can play decides what every
/// screen shows, so qualification is not something the Library happens to do first.
export function useClientQualification(account: Account) {
  const queryClient = useQueryClient()
  const profiles = useQuery({
    queryKey: queryKeys.playbackProfiles,
    queryFn: api.unassessedPlaybackProfiles,
    staleTime: 60_000,
  })
  const report = useMutation({
    mutationFn: (assessments: ClientPlaybackAssessmentReport[]) =>
      api.recordPlaybackAssessments(assessments, account.csrfToken),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['videos'] })
      void queryClient.invalidateQueries({ queryKey: ['video'] })
      void queryClient.invalidateQueries({ queryKey: queryKeys.personalLibrary })
      void queryClient.invalidateQueries({ queryKey: queryKeys.playbackProfiles })
    },
  })
  const pending = report.isPending
  const outstanding = profiles.data

  useEffect(() => {
    if (!outstanding || outstanding.length === 0 || pending) return
    let cancelled = false
    void Promise.all(outstanding.map(assessProfile)).then((assessments) => {
      if (!cancelled && assessments.length > 0) {
        report.mutate(assessments)
      }
    })
    return () => { cancelled = true }
    // The mutation is intentionally not a dependency: it changes identity on every render, and
    // one round of qualification per set of outstanding profiles is what this owes the library.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [outstanding, pending])

  return report.isError
}

/// What this browser makes of one media configuration.
async function assessProfile(
  profile: UnassessedPlaybackProfile,
): Promise<ClientPlaybackAssessmentReport> {
  const capabilities = navigator.mediaCapabilities

  if (capabilities && profile.videoContentType) {
    try {
      const support = await capabilities.decodingInfo({
        type: 'file',
        video: {
          contentType: profile.videoContentType,
          width: Number(profile.width ?? 1280),
          height: Number(profile.height ?? 720),
          bitrate: Number(profile.bitrate ?? 2_000_000),
          framerate: Number(profile.frameRate ?? 25),
        },
        ...(profile.audioContentType
          ? {
            audio: {
              contentType: profile.audioContentType,
              channels: String(profile.audioChannels ?? 2),
              bitrate: Number(profile.audioBitrate ?? 128_000),
              samplerate: Number(profile.audioSampleRate ?? 48_000),
            },
          }
          : {}),
      })

      return {
        profileKey: profile.profileKey,
        verdict: support.supported ? 'Positive' : 'Negative',
        smooth: support.smooth ?? null,
        powerEfficient: support.powerEfficient ?? null,
        method: 'MediaCapabilities',
      }
    } catch {
      // A configuration this browser cannot even be asked about is not an answer either way.
    }
  }

  const probe = document.createElement('video')
  const answer = profile.basicContentType ? probe.canPlayType(profile.basicContentType) : ''

  return {
    profileKey: profile.profileKey,
    // `maybe` is the browser declining to commit, which is indeterminate rather than a refusal.
    verdict: answer === 'probably' ? 'Positive' : answer === 'maybe' ? 'Indeterminate' : 'Negative',
    smooth: null,
    powerEfficient: null,
    method: 'CanPlayType',
  }
}
