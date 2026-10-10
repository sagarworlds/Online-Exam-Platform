import ngswConfig from '../../../../ngsw-config.json';

/**
 * Whether a request path matches one of the service worker's file patterns. `*` stands for part of a path segment, `**` for any number
 * of segments, and `(a|b)` for either alternative, which is how Angular's service worker reads them.
 */
function matches(pattern: string, path: string): boolean {
  const source = pattern
    .split('**')
    .map((part) => part.replace(/[.+?^${}[\]\\]/g, '\\$&').replace(/\*/g, '[^/]*').replace(/\(/g, '(?:'))
    .join('.*');
  return new RegExp(`^${source}$`).test(path);
}

const filePatternsOf = (group: { resources: { files: string[] } }): string[] => group.resources.files;

describe('PWA: the service worker (NFR-8, NFR-5)', () => {
  describe('the service worker', () => {
    // Why these tests exist: the service worker must cache the app shell only. An exam paper and its pictures are readable only once the
    // exam starts (NFR-5), so a copy kept by the browser would outlive that rule. Every response the API gives is a path under /api.
    const apiPaths = [
      '/api/v1/me/attempts/a1',
      '/api/v1/me/attempts/a1/questions/q1/pictures/q-0',
      '/api/v1/me/attempts/a1/questions/q1/pictures/q-0.png',
      '/api/v1/exams/e1/attempts/a1/paper',
      '/api/v1/exams/e1/attempts/a1/paper.json',
    ];

    it('caches no API response, because it has no data groups and its file patterns do not reach /api', () => {
      expect('dataGroups' in ngswConfig).toBe(false);

      // A positive control: the broad pattern this policy replaced would reach a picture served under /api, so the matcher must say so.
      expect(matches('/**/*.(svg|png)', '/api/v1/me/attempts/a1/questions/q1/pictures/q-0.png')).toBe(true);

      const patterns = ngswConfig.assetGroups.flatMap(filePatternsOf);
      for (const path of apiPaths) {
        expect(patterns.some((pattern) => matches(pattern, path)), path).toBe(false);
      }
    });

    it('caches only the app shell, the app icons and the media the build bundles', () => {
      const shell = ngswConfig.assetGroups.find((group) => group.name === 'app');
      const assets = ngswConfig.assetGroups.find((group) => group.name === 'assets');

      expect(shell && filePatternsOf(shell)).toEqual([
        '/favicon.ico',
        '/index.csr.html',
        '/index.html',
        '/manifest.webmanifest',
        '/*.css',
        '/*.js',
      ]);
      expect(assets && filePatternsOf(assets)).toEqual(['/icons/**', '/media/**']);
    });

    it('never answers a navigation to an API path with the app shell', () => {
      expect(ngswConfig.navigationUrls).toContain('!/api/**');
    });
  });
});
