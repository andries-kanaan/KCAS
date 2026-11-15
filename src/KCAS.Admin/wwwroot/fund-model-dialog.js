export function open(dialog) {
    if (!dialog.open) dialog.showModal();
    dialog.querySelector('[data-cancel]')?.focus();
}
export function close(dialog) {
    if (dialog.open) dialog.close();
}
