const CONSENT_KEY = 'signaleire_cookie_consent';
document.addEventListener('DOMContentLoaded', () => {
    if (!localStorage.getItem(CONSENT_KEY)) {
        document.getElementById('cookie-banner')?.removeAttribute('hidden');
    }
});
function acceptCookies() {
    localStorage.setItem(CONSENT_KEY, 'accepted');
    document.getElementById('cookie-banner')?.setAttribute('hidden', '');
}
function declineCookies() {
    localStorage.setItem(CONSENT_KEY, 'declined');
    document.getElementById('cookie-banner')?.setAttribute('hidden', '');
}
