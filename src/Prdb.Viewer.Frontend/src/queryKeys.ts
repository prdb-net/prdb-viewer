export const queryKeys = {
  state: ['access-state'] as const,
  account: ['account'] as const,
  accounts: ['accounts'] as const,
  configuration: ['configuration'] as const,
  libraryDirectoryCandidates: ['library-directory-candidates'] as const,
  backgroundWork: ['background-work'] as const,
  workIssueItems: (workIssueId: string) => ['work-issue-items', workIssueId] as const,
  identificationQueue: ['identification-queue'] as const,
  identificationCase: (videoId: string) => ['identification-case', videoId] as const,
  /// The revealed depth is not part of the key: `useInfiniteQuery` holds the pages of one search
  /// under one entry, so revealing more adds a page rather than replacing the search.
  videos: (filters: string) => ['videos', filters] as const,
  video: (videoId: string) => ['video', videoId] as const,
  /// The facets are counted against the current narrowing, so they are keyed by it.
  // Looking for a value inside a facet changes the answer without changing the narrowing, so it
  // belongs in the key beside it rather than folded into it.
  libraryFacets: (narrowing: string, finding: string) =>
    ['library-facets', narrowing, finding] as const,
  playbackProfiles: ['playback-profiles'] as const,
  /// Mutation keys rather than query keys: they are how the subjects with something in flight are
  /// found in the mutation cache, so one row can be busy without the screen being busy.
  personalAction: ['personal-action'] as const,
  accountAction: ['account-action'] as const,
}
