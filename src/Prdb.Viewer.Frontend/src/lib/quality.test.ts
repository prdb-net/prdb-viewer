import { variant } from '../test/fixtures'
import {
  audioSummary,
  formatBitrate,
  formatRuntime,
  formatSize,
  qualityBandLabel,
  qualityFacts,
  qualityLabel,
} from './quality'

/// Naming what the Core decided, and stating the facts around it the way the world writes them.
///
/// Which band a picture belongs to is not decided here: that rule lives in the Core, travels on the
/// wire, and is exercised by `VideoQualityRuleTests`. Deriving it a second time in the browser is
/// exactly how a filter and a card come to disagree.

describe('quality', () => {
  it('names each band the way a release is named', () => {
    expect(qualityBandLabel('Uhd4320')).toBe('8K')
    expect(qualityBandLabel('Uhd2160')).toBe('4K')
    expect(qualityBandLabel('Qhd1440')).toBe('1440p')
    expect(qualityBandLabel('FullHd1080')).toBe('1080p')
    expect(qualityBandLabel('Hd720')).toBe('720p')
    expect(qualityBandLabel('StandardDefinition')).toBe('SD')
  })

  it('claims nothing where inspection established no dimensions', () => {
    expect(qualityBandLabel('Unknown')).toBeUndefined()
    expect(qualityLabel(variant({ qualityBand: 'Unknown', frameRate: null }))).toBeUndefined()
  })

  it('names a frame rate only where it is one worth naming', () => {
    expect(qualityLabel(variant({ frameRate: 25 }))).toBe('1080p')
    expect(qualityLabel(variant({ frameRate: 23.976 }))).toBe('1080p')
    expect(qualityLabel(variant({ frameRate: 59.94 }))).toBe('1080p · 60 fps')
  })

  it('states sizes, rates and audio the way the rest of the world writes them', () => {
    expect(formatSize(4_200_000_000)).toBe('4.2 GB')
    expect(formatSize(750_000)).toBe('750 kB')
    expect(formatSize(0)).toBeUndefined()

    expect(formatBitrate(8_123_456)).toBe('8.1 Mbit/s')
    expect(formatBitrate(128_000)).toBe('128 kbit/s')
    expect(formatBitrate(0)).toBeUndefined()

    expect(formatRuntime(5_400_000)).toBe('1 h 30 min')
    expect(formatRuntime(600_000)).toBe('10 min')

    expect(audioSummary(variant({ audioCodec: 'aac', audioChannels: 6, audioSampleRate: 48_000 })))
      .toBe('aac · 5.1 · 48 kHz')
    expect(audioSummary(variant({ audioCodec: null }))).toBeUndefined()
  })

  it('leaves out a fact inspection could not establish rather than printing it blank', () => {
    const facts = qualityFacts(variant({ bitrate: null, audioCodec: null, size: 0 }))

    expect(facts.map((fact) => fact.label)).toEqual(['Quality', 'Runtime'])
  })
})
