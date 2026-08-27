export const BROWSER_CACHE_CLEAR_PATH = '/__basis/clear-browser-cache';

const BROWSER_CACHE_CONTROLS = `
<button id="basis-clear-browser-cache" type="button">キャッシュを破棄して再読み込み</button>
<style>
  #basis-clear-browser-cache {
    position: fixed;
    top: 12px;
    right: 12px;
    z-index: 10000;
    padding: 8px 12px;
    border: 1px solid #666;
    border-radius: 6px;
    background: #fff;
    color: #222;
    cursor: pointer;
  }
  #basis-clear-browser-cache:disabled {
    cursor: wait;
    opacity: 0.7;
  }
</style>
<script>
  document.querySelector('#basis-clear-browser-cache').addEventListener('click', async (event) => {
    const button = event.currentTarget;
    button.disabled = true;
    button.textContent = 'キャッシュを破棄中…';
    try {
      const response = await fetch('${BROWSER_CACHE_CLEAR_PATH}', {
        method: 'POST',
        cache: 'no-store',
        credentials: 'same-origin'
      });
      if (!response.ok) throw new Error('Cache clear request failed');
      if ('caches' in window) {
        const cacheNames = await caches.keys();
        await Promise.all(cacheNames.map(cacheName => caches.delete(cacheName)));
      }
      const reloadUrl = new URL(window.location.href);
      reloadUrl.searchParams.set('cache-reset', Date.now().toString());
      window.location.replace(reloadUrl);
    } catch {
      button.disabled = false;
      button.textContent = 'キャッシュの破棄に失敗しました';
    }
  });
</script>`;

export function injectBrowserCacheControls(html: string): string {
  const bodyEnd = html.lastIndexOf('</body>');
  if (bodyEnd < 0) throw new Error('Unity index.html does not contain a closing body tag.');
  return `${html.slice(0, bodyEnd)}${BROWSER_CACHE_CONTROLS}\n${html.slice(bodyEnd)}`;
}

export function browserCacheClearResponse(request: Request): Response {
  if (request.method !== 'POST') {
    return new Response('Method Not Allowed', {
      status: 405,
      headers: { allow: 'POST' },
    });
  }

  return new Response('Browser cache cleared', {
    headers: {
      'cache-control': 'no-store',
      'clear-site-data': '"cache"',
      'content-type': 'text/plain; charset=utf-8',
    },
  });
}
