(() => {
  if (window.__troTotSpaInitialized) return;
  window.__troTotSpaInitialized = true;

  const getPage = (documentRoot = document) => documentRoot.querySelector('#spa-page');
  const setLoading = (loading) => document.body.classList.toggle('spa-loading', loading);

  async function executePageScripts(root) {
    const scripts = Array.from(root.querySelectorAll('script'));
    for (const oldScript of scripts) {
      const script = document.createElement('script');
      for (const attribute of oldScript.attributes) {
        script.setAttribute(attribute.name, attribute.value);
      }
      script.textContent = oldScript.textContent;
      oldScript.replaceWith(script);
      if (script.src && !script.async) {
        await new Promise((resolve) => {
          script.addEventListener('load', resolve, { once: true });
          script.addEventListener('error', resolve, { once: true });
        });
      }
    }
  }

  function updatePageStyles(nextDocument) {
    document.head.querySelectorAll('link[data-spa-stylesheet]').forEach((link) => link.remove());
    nextDocument.head.querySelectorAll('link[rel="stylesheet"]').forEach((source) => {
      const stylesheetUrl = new URL(source.href, window.location.href);
      const localStylesheet = stylesheetUrl.origin === window.location.origin &&
        stylesheetUrl.pathname.startsWith('/css/');
      const leafletStylesheet = stylesheetUrl.origin === 'https://unpkg.com' &&
        stylesheetUrl.pathname === '/leaflet@1.9.4/dist/leaflet.css';
      if (!localStylesheet && !leafletStylesheet) return;
      const link = source.cloneNode(true);
      link.dataset.spaStylesheet = '';
      document.head.appendChild(link);
    });
    document.head.querySelectorAll('[data-spa-page-style]').forEach((style) => style.remove());
    nextDocument.head.querySelectorAll('style').forEach((source) => {
      const style = document.createElement('style');
      style.dataset.spaPageStyle = '';
      style.textContent = source.textContent;
      document.head.appendChild(style);
    });
  }

  async function renderUrl(url, { historyMode = 'push', method = 'GET', body } = {}) {
    setLoading(true);
    try {
      const response = await fetch(url, {
        method,
        body,
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
        credentials: 'same-origin'
      });
      const contentType = response.headers.get('content-type') || '';
      if (!contentType.includes('text/html')) {
        window.location.assign(response.url || url);
        return;
      }

      const html = await response.text();
      const nextDocument = new DOMParser().parseFromString(html, 'text/html');
      const currentPage = getPage();
      const nextPage = getPage(nextDocument);
      if (!currentPage || !nextPage) {
        window.location.assign(response.url || url);
        return;
      }

      updatePageStyles(nextDocument);
      currentPage.replaceWith(nextPage);
      document.title = nextDocument.title;
      const currentDescription = document.head.querySelector('meta[name="description"]');
      const nextDescription = nextDocument.head.querySelector('meta[name="description"]');
      if (currentDescription && nextDescription) {
        currentDescription.content = nextDescription.content;
      } else if (nextDescription) {
        document.head.appendChild(nextDescription.cloneNode(true));
      } else {
        currentDescription?.remove();
      }

      if (historyMode === 'push') {
        history.pushState({}, '', response.url || url);
      } else if (historyMode === 'replace') {
        history.replaceState({}, '', response.url || url);
      }

      await executePageScripts(nextPage);
      document.dispatchEvent(new CustomEvent('troto:page-loaded'));

      const hash = new URL(response.url || url, window.location.href).hash;
      if (hash) {
        document.getElementById(decodeURIComponent(hash.slice(1)))?.scrollIntoView();
      } else {
        window.scrollTo(0, 0);
      }
    } catch (error) {
      console.error('Điều hướng AJAX thất bại.', error);
      if (method === 'GET') {
        window.location.assign(url);
      } else {
        const message = document.createElement('div');
        message.className = 'spa-error-message';
        message.setAttribute('role', 'alert');
        message.textContent = 'Không gửi được dữ liệu. Vui lòng kiểm tra kết nối và thử lại.';
        getPage()?.prepend(message);
      }
    } finally {
      setLoading(false);
    }
  }

  window.__troTotNavigate = (url) => renderUrl(url);

  document.addEventListener('click', (event) => {
    if (event.defaultPrevented || event.button !== 0 ||
        event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

    const anchor = event.target.closest('a[href]');
    if (!anchor || anchor.target || anchor.hasAttribute('download') ||
        anchor.dataset.noSpa !== undefined || anchor.getAttribute('href').startsWith('#')) return;

    const target = new URL(anchor.href, window.location.href);
    if (target.origin !== window.location.origin ||
        !['http:', 'https:'].includes(target.protocol) ||
        target.pathname.startsWith('/api/')) return;

    event.preventDefault();
    renderUrl(target.href);
  });

  document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form.dataset.noSpa !== undefined ||
        form.hasAttribute('data-ajax-ignore')) return;

    const method = (form.method || 'GET').toUpperCase();
    const target = new URL(form.action || window.location.href, window.location.href);
    if (target.origin !== window.location.origin ||
        (method !== 'GET' && method !== 'POST') ||
        (form.enctype || '').toLowerCase().includes('multipart/form-data')) return;

    event.preventDefault();
    const formData = new FormData(form);
    if (method === 'GET') {
      for (const [key, value] of formData.entries()) {
        if (typeof value === 'string') target.searchParams.append(key, value);
      }
      renderUrl(target.href);
      return;
    }

    renderUrl(target.href, { method, body: formData });
  });

  window.addEventListener('popstate', () => renderUrl(window.location.href, { historyMode: 'none' }));
})();
