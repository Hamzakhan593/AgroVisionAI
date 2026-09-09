(() => {
    if (window.agroCareLanguageReady) return;
    window.agroCareLanguageReady = true;
    const apply = (language) => {
        const selected = language === 'en' ? 'en' : 'ur';
        document.documentElement.dataset.careLanguage = selected;
        document.querySelectorAll('[data-care-set-language]').forEach(button => {
            button.setAttribute('aria-pressed', String(button.dataset.careSetLanguage === selected));
        });
        try { localStorage.setItem('agro-care-language', selected); } catch (_) { /* Private mode: use this page only. */ }
    };
    let saved = 'ur';
    try { saved = localStorage.getItem('agro-care-language') || 'ur'; } catch (_) { /* Default to Roman Urdu. */ }
    apply(saved);
    document.addEventListener('click', event => {
        const button = event.target.closest('[data-care-set-language]');
        if (button) apply(button.dataset.careSetLanguage);
    });
})();
