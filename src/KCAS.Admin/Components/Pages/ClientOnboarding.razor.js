export async function copyBrief(element) {
    if (!element || typeof element.value !== "string") return false;

    if (globalThis.isSecureContext && navigator.clipboard?.writeText) {
        try {
            await navigator.clipboard.writeText(element.value);
            return true;
        } catch {
            // Browser permissions can block the clipboard API; try copying the selection.
        }
    }

    const previousFocus = document.activeElement;
    element.focus({ preventScroll: true });
    element.select();
    element.setSelectionRange(0, element.value.length);
    let copied = false;
    try { copied = document.execCommand("copy"); } catch { }
    if (copied) previousFocus?.focus({ preventScroll: true });
    return copied;
}
