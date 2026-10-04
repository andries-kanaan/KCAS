document.addEventListener("click", event => {
    document.querySelectorAll(".workspace-action-menu[open]").forEach(menu => {
        if (!menu.contains(event.target) || event.target.closest(".workspace-action-list a, .workspace-action-list button")) {
            menu.open = false;
        }
    });
});

document.addEventListener("keydown", event => {
    if (event.key !== "Escape") return;
    document.querySelectorAll(".workspace-action-menu[open]").forEach(menu => {
        if (menu.contains(document.activeElement)) menu.querySelector("summary").focus();
        menu.open = false;
    });
});
