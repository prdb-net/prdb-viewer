import {
  fileFormat,
  missingPreviewLabel,
  missingPreviewReason,
  playbackUnavailableReason,
  variantReason,
} from './format'
import type { PlaybackVariant, VideoSummary } from '../api/client'

/// What a Video with no browser path says about itself.
///
/// The sentence used to be "Not directly playable: matroska,webm (h264 + aac) needs conversion,
/// which this product deliberately does not do", and it defeated the reader three times over: the
/// container was named by the demuxer's format list rather than by its name, the codecs were put
/// forward as the problem when they are what every browser plays, and the only thing said plainly
/// was a fact about the product rather than about the file.
describe('why a Video cannot be played here', () => {
  it('names the container when the codecs are ones browsers play', () => {
    const reason = playbackUnavailableReason(unsupported({
      containerName: 'Matroska',
      videoCodec: 'h264',
      audioCodec: 'aac',
      directPlayObstacle: 'Container',
    }))

    expect(reason).toContain('No browser reads the Matroska container')
    // The actionable half: nothing is wrong with the picture or the sound.
    expect(reason).toContain('h264 and aac streams are ones browsers do play')
    expect(reason).toContain('the container alone is what stands in the way')
    // The probe's own list never reaches a reader again.
    expect(reason).not.toContain('matroska,webm')
  })

  it('names the codecs when no container would save them', () => {
    const reason = playbackUnavailableReason(unsupported({
      containerName: 'MP4',
      videoCodec: 'msmpeg4v3',
      audioCodec: 'aac',
      directPlayObstacle: 'Codecs',
    }))

    expect(reason).toContain('No supported browser decodes msmpeg4v3 and aac')
    expect(reason).toContain('whichever container carries them')
  })

  it('names both where both are in the way', () => {
    const reason = playbackUnavailableReason(unsupported({
      containerName: 'AVI',
      videoCodec: 'mpeg4',
      audioCodec: null,
      directPlayObstacle: 'ContainerAndCodecs',
    }))

    expect(reason).toContain('Neither the AVI container nor its mpeg4 streams')
  })

  it('says the product does not convert once, after the file has been accounted for', () => {
    const reason = playbackUnavailableReason(unsupported({
      containerName: 'Matroska',
      directPlayObstacle: 'Container',
    }))

    // The boundary is kept — nothing here may suggest the product might convert the file — but it
    // is the last sentence rather than the whole answer.
    expect(reason).toMatch(/This product does not convert video, so the file is offered as it is/)
    expect(reason.indexOf('No browser reads')).toBeLessThan(reason.indexOf('does not convert'))
  })

  it('says it once for two occurrences that are held back by the same thing', () => {
    const reason = playbackUnavailableReason(unsupported(
      { containerName: 'Matroska', directPlayObstacle: 'Container' },
      { containerName: 'Matroska', directPlayObstacle: 'Container' },
    ))

    expect(reason.match(/No browser reads/g)).toHaveLength(1)
  })

  it('still says which client refused a Video that is not Unsupported', () => {
    const video = unsupported({ containerName: 'MP4', directPlayObstacle: 'None' })

    expect(playbackUnavailableReason({ ...video, isUnsupportedVideo: false }))
      .toContain('This browser did not play MP4')
  })
})

describe('what one occurrence is', () => {
  it('names the container rather than listing the demuxer formats', () => {
    expect(fileFormat(file({ containerName: 'Matroska', videoCodec: 'h264', audioCodec: 'aac' })))
      .toBe('Matroska (h264 + aac)')
  })

  /// A file the installation ruled out from its own bytes read "not assessed yet", which promised
  /// an assessment that was never going to be asked for.
  it('does not promise an assessment for a file with no browser path', () => {
    expect(variantReason(file({ selectionReason: 'NoBrowserPath' }))).toBe('no browser path')
    expect(variantReason(file({ selectionReason: 'NotYetAssessed' }))).toBe('not assessed yet')
  })
})

function file(overrides: Partial<PlaybackVariant> = {}) {
  return {
    videoFileId: '01994dd4-2a0a-7000-8000-000000000011',
    containerFormat: 'matroska,webm',
    containerName: 'Matroska',
    videoCodec: 'h264',
    audioCodec: 'aac',
    directPlayClassification: 'Unsupported',
    directPlayObstacle: 'ContainerAndCodecs',
    selectionReason: 'NoBrowserPath',
    outcome: null,
    ...overrides,
  } as unknown as PlaybackVariant
}

function unsupported(...files: Partial<PlaybackVariant>[]) {
  return {
    isUnsupportedVideo: true,
    videoFiles: files.map(file),
  } as unknown as VideoSummary
}

/// The three ways a Video can have no picture, which used to be one grey triangle and no words.
describe('why a Video has no picture', () => {
  it('says a preview is still to come while the lane has not reached it', () => {
    expect(missingPreviewReason('Pending')).toBe(
      'A preview for this Video has not been generated yet.')
    expect(missingPreviewLabel('Pending')).toBe('Preview not generated yet')
  })

  it('says the attempt was made and produced nothing, and that playback is unaffected', () => {
    expect(missingPreviewReason('NoFrame')).toContain('No frame could be read')
    expect(missingPreviewReason('NoFrame')).toContain('Playback is unaffected')
    expect(missingPreviewLabel('NoFrame')).toBe('No preview could be made from this Video')
  })

  it('says an unreadable file is a question about where the file is', () => {
    expect(missingPreviewReason('Unreachable')).toContain('cannot be read')
    expect(missingPreviewLabel('Unreachable')).toBe('No preview while this Video cannot be read')
  })

  it('says nothing about a Video that has its picture', () => {
    expect(missingPreviewReason('Present')).toBeUndefined()
  })
})
