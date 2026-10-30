'use strict';
// Another tab can change the account cookie after this form was opened.
// Fetch a token for the current session before sending the credentials.
document.querySelectorAll('form[data-account-form]').forEach(form => {
  let submitting = false;
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (submitting) return;
    submitting = true;
    const button = form.querySelector('button[type=submit]');
    const error = form.querySelector('[data-account-error]');
    button.disabled = true;
    error.hidden = true;
    try {
      const url = new URL(form.action || location.href);
      url.searchParams.set('handler', 'Token');
      const response = await fetch(url, { cache: 'no-store', credentials: 'same-origin' });
      if (!response.ok) throw new Error('Unable to refresh your session. Reload this page and try again.');
      const { token } = await response.json();
      if (!token) throw new Error('Unable to refresh your session. Reload this page and try again.');
      form.querySelector('[name=__RequestVerificationToken]').value = token;
      HTMLFormElement.prototype.submit.call(form);
    } catch (failure) {
      error.textContent = failure.message;
      error.hidden = false;
      button.disabled = false;
      submitting = false;
    }
  });
});
