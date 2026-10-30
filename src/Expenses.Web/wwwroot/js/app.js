'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const money = value => new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(value / 100);
  const decimal = value => (value / 100).toFixed(2);
  let session, group, editingId, paymentTo, groupList = [];
  const name = id => group.members.find(m => m.userId === id)?.email ?? 'Member';
  const notice = (message, error = false) => { $('notice').textContent = message; $('notice').classList.toggle('error', error); $('notice').hidden = false; };
  function paise(value) {
    if (!/^\d{1,9}(\.\d{1,2})?$/.test(value.trim())) throw new Error('Use a positive amount with at most two decimal places.');
    const [whole, fraction = ''] = value.trim().split('.');
    return Number(whole) * 100 + Number(fraction.padEnd(2, '0'));
  }
  async function request(path, method = 'GET', body) {
    const response = await fetch('/api' + path, { method, headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session?.csrfToken ?? '' }, body: body === undefined ? undefined : JSON.stringify(body) });
    if (response.status === 401 || response.redirected) { location.href = '/Identity/Account/Login'; throw new Error('Please sign in again.'); }
    if (!response.ok) {
      const result = await response.json().catch(() => ({}));
      if (response.status === 409 && group) await loadGroup(group.id);
      throw new Error(result.error ?? 'Something went wrong. Please try again.');
    }
    return response.status === 204 ? null : response.json();
  }
  function showDialog(id) { const d = $(id); d.querySelector('.form-error').textContent = ''; d.showModal(); }
  document.querySelectorAll('[data-close]').forEach(b => b.addEventListener('click', () => b.closest('dialog').close()));
  function bindForm(id, action) {
    $(id).addEventListener('submit', async event => {
      event.preventDefault();
      const form = event.currentTarget, button = form.querySelector('[type=submit]');
      button.disabled = true;
      form.querySelector('.form-error').textContent = '';
      try { await action(form); form.closest('dialog').close(); }
      catch (error) { form.querySelector('.form-error').textContent = error.message; }
      finally { button.disabled = false; }
    });
  }
  async function loadGroups(preferred) {
    groupList = await request('/groups');
    $('groups').innerHTML = groupList.map(g => `<button class="group-link ${g.id === preferred ? 'active' : ''}" data-group="${g.id}">${escape(g.name)}<small>${g.memberCount} ${g.memberCount === 1 ? 'person' : 'people'}</small></button>`).join('');
    $('empty').hidden = groupList.length > 0;
    if (groupList.length) await loadGroup(groupList.some(g => g.id === preferred) ? preferred : groupList[0].id);
  }
  async function loadGroup(id) {
    group = await request('/groups/' + id);
    history.replaceState(null, '', '/?group=' + id);
    document.querySelectorAll('[data-group]').forEach(b => b.classList.toggle('active', Number(b.dataset.group) === id));
    $('group').hidden = false; $('empty').hidden = true;
    $('group-name').textContent = group.name;
    $('member-count').textContent = `${group.members.length} ${group.members.length === 1 ? 'person' : 'people'} · A shared view of every expense`;
    const balance = group.balances[session.userId];
    $('owed').textContent = money(Math.max(balance, 0));
    $('owing').textContent = money(Math.max(-balance, 0));
    $('total').textContent = money(group.expenses.reduce((n, e) => n + e.amount, 0));
    $('expenses').innerHTML = group.expenses.map(e => {
      const editable = e.createdById === session.userId || group.ownerId === session.userId;
      const yourShare = e.shares.find(s => s.userId === session.userId)?.amount ?? 0;
      return `<article class="expense-row"><div class="expense-info"><span class="expense-icon" aria-hidden="true">↗</span><div><strong>${escape(e.description)}</strong><p>${escape(e.date)} · ${escape(name(e.paidById))} paid</p><p>Your share ${money(yourShare)} · ${e.shares.length} participants</p></div></div><div class="expense-amount"><strong>${money(e.amount)}</strong>${editable ? `<div><button class="text-button" data-edit="${e.id}">Edit</button><button class="text-button" data-delete="${e.id}">Delete</button></div>` : ''}</div></article>`;
    }).join('') || '<p class="empty-note">Nothing to split yet. Add your first expense.</p>';
    $('members').innerHTML = group.members.map(m => `<div class="person-row"><span>${escape(m.email)}${m.userId === session.userId ? ' (you)' : ''}</span><strong>${money(group.balances[m.userId])}</strong></div>`).join('');
    $('debts').innerHTML = group.debts.map(d => `<div class="debt-row"><div>${escape(name(d.fromId))}<br /><span class="muted">pays ${escape(name(d.toId))}</span>${d.fromId === session.userId ? `<button data-pay="${escape(d.toId)}" data-amount="${d.amount}">Record payment</button>` : ''}</div><strong>${money(d.amount)}</strong></div>`).join('') || '<p class="empty-note">All settled. Enjoy the good company.</p>';
    $('settlements').innerHTML = group.settlements.map(s => `<div class="payment-row"><div>${escape(name(s.fromId))} paid ${escape(name(s.toId))}<p>${escape(new Date(s.createdAt).toLocaleDateString())}</p></div><strong>${money(s.amount)}</strong></div>`).join('') || '<p class="empty-note">Payments recorded by members appear here.</p>';
  }
  function openExpense(expense) {
    editingId = expense?.id;
    const form = $('expense-form'); form.reset();
    $('expense-title').textContent = expense ? 'Edit expense' : 'Add an expense';
    form.elements.description.value = expense?.description ?? '';
    form.elements.amount.value = expense ? decimal(expense.amount) : '';
    form.elements.date.value = expense?.date ?? new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);
    form.elements.paidById.innerHTML = group.members.map(m => `<option value="${escape(m.userId)}">${escape(m.email)}</option>`).join('');
    form.elements.paidById.value = expense?.paidById ?? session.userId;
    form.elements.split.value = expense ? 'custom' : 'equal';
    $('participants').innerHTML = group.members.map(m => {
      const share = expense?.shares.find(s => s.userId === m.userId);
      return `<label class="participant"><input type="checkbox" value="${escape(m.userId)}" ${!expense || share ? 'checked' : ''} /><span>${escape(m.email)}</span><input class="share-input" type="text" inputmode="decimal" value="${share ? decimal(share.amount) : ''}" aria-label="Share for ${escape(m.email)}" ${expense ? '' : 'hidden'} /></label>`;
    }).join('');
    showDialog('expense-dialog');
  }
  $('expense-form').elements.split.addEventListener('change', event => document.querySelectorAll('.share-input').forEach(i => { i.hidden = event.target.value !== 'custom'; }));
  const openGroup = () => { $('group-form').reset(); showDialog('group-dialog'); };
  $('new-group').onclick = openGroup; $('first-group').onclick = openGroup;
  $('invite').onclick = () => { $('invite-form').reset(); showDialog('invite-dialog'); };
  $('add-expense').onclick = () => openExpense();
  $('refresh').onclick = () => loadGroup(group.id).catch(e => notice(e.message, true));
  $('groups').onclick = event => { const b = event.target.closest('[data-group]'); if (b) loadGroup(Number(b.dataset.group)).catch(e => notice(e.message, true)); };
  $('expenses').onclick = async event => {
    const edit = event.target.closest('[data-edit]'), remove = event.target.closest('[data-delete]');
    if (edit) openExpense(group.expenses.find(e => e.id === Number(edit.dataset.edit)));
    if (remove && confirm('Delete this expense? Balances will be recalculated and affected members notified.')) {
      remove.disabled = true;
      try { await request(`/groups/${group.id}/expenses/${remove.dataset.delete}?version=${group.version}`, 'DELETE'); await loadGroup(group.id); notice('Expense deleted.'); }
      catch (error) { notice(error.message, true); remove.disabled = false; }
    }
  };
  $('debts').onclick = event => {
    const b = event.target.closest('[data-pay]'); if (!b) return;
    paymentTo = b.dataset.pay;
    $('payment-to').textContent = 'Payment to ' + name(paymentTo);
    $('settlement-form').elements.amount.value = decimal(Number(b.dataset.amount));
    showDialog('settlement-dialog');
  };
  bindForm('group-form', async form => {
    const created = await request('/groups', 'POST', { name: form.elements.name.value });
    await loadGroups(created.id); notice('Your group is ready. Invite someone to get started.');
  });
  bindForm('invite-form', async form => {
    await request(`/groups/${group.id}/invitations`, 'POST', { email: form.elements.email.value, version: group.version });
    await loadGroup(group.id); notice('Invitation saved for email delivery.');
  });
  bindForm('expense-form', async form => {
    const rows = [...document.querySelectorAll('.participant')].filter(r => r.querySelector('[type=checkbox]').checked);
    const participants = rows.map(r => r.querySelector('[type=checkbox]').value);
    const customShares = form.elements.split.value === 'custom' ? Object.fromEntries(rows.map(r => [r.querySelector('[type=checkbox]').value, paise(r.querySelector('.share-input').value)])) : null;
    await request(`/groups/${group.id}/expenses${editingId ? '/' + editingId : ''}`, editingId ? 'PUT' : 'POST', {
      description: form.elements.description.value, amount: paise(form.elements.amount.value), paidById: form.elements.paidById.value,
      date: form.elements.date.value, participants, customShares, version: group.version
    });
    await loadGroup(group.id); notice('Expense saved. Balances updated.');
  });
  bindForm('settlement-form', async form => {
    await request(`/groups/${group.id}/settlements`, 'POST', { toId: paymentTo, amount: paise(form.elements.amount.value), version: group.version });
    await loadGroup(group.id); notice('Payment recorded.');
  });
  (async () => { session = await request('/session'); await loadGroups(Number(new URLSearchParams(location.search).get('group'))); })().catch(e => notice(e.message, true));
})();
