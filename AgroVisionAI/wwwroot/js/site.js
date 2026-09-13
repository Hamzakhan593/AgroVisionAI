document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll(".app-navbar .nav-link-custom[href], .app-nav-dropdown a[href]").forEach(link => {
        const url = new URL(link.href);
        if (!url.hash && url.pathname.toLowerCase() === location.pathname.toLowerCase()) {
            link.classList.add("active");
            link.setAttribute("aria-current", "page");
            link.closest(".dropdown")?.querySelector(".dropdown-toggle")?.classList.add("active");
        }
    });
    const form = document.getElementById("detectionForm");
    if (!form) return;
    const input = document.getElementById("cropImage");
    const preview = document.getElementById("imagePreview");
    const container = document.getElementById("imagePreviewContainer");
    const button = document.getElementById("analyzeButton");
    const remove = document.getElementById("removeImage");
    const feedback = document.getElementById("uploadFeedback");
    const drop = document.getElementById("dropZone");
    let objectUrl = null;
    let submitting = false;
    function clear() {
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        objectUrl = null;
        input.value = "";
        preview.removeAttribute("src");
        container.classList.remove("visible");
        button.disabled = true;
    }
    function showFile() {
        const file = input.files[0];
        feedback.textContent = "";
        if (!file) { clear(); return; }
        if (!/\.(jpe?g|png)$/i.test(file.name) || file.size === 0 || file.size > 5 * 1024 * 1024) {
            clear(); feedback.textContent = "Choose a non-empty JPG or PNG image, 5 MB or smaller."; return;
        }
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        objectUrl = URL.createObjectURL(file);
        button.disabled = true;
        preview.onload = () => {
            if (preview.naturalWidth < 32 || preview.naturalHeight < 32) {
                clear(); feedback.textContent = "Choose an image at least 32 × 32 pixels."; return;
            }
            container.classList.add("visible"); button.disabled = false;
        };
        preview.onerror = () => { clear(); feedback.textContent = "This image cannot be opened. Please choose another."; };
        preview.src = objectUrl;
        document.getElementById("imageFileName").textContent = file.name;
    }
    input.addEventListener("change", showFile);
    remove.addEventListener("click", () => { if (!submitting) { clear(); feedback.textContent = "Image removed."; input.focus(); } });
    ["dragenter", "dragover"].forEach(name => drop.addEventListener(name, event => {
        event.preventDefault(); if (!submitting) drop.classList.add("drag-active");
    }));
    ["dragleave", "drop"].forEach(name => drop.addEventListener(name, event => {
        event.preventDefault(); drop.classList.remove("drag-active");
    }));
    drop.addEventListener("drop", event => {
        if (submitting) return;
        if (event.dataTransfer.files.length !== 1) { feedback.textContent = "Drop one image at a time."; return; }
        input.files = event.dataTransfer.files; showFile();
    });
    form.addEventListener("submit", event => {
        if (submitting || !input.files.length || button.disabled) { event.preventDefault(); return; }
        submitting = true; button.disabled = true; remove.disabled = true;
        form.setAttribute("aria-busy", "true");
        button.querySelector("span").textContent = "Identifying crop and checking leaf…";
        button.querySelector("i").className = "bi bi-arrow-repeat analyze-spinner";
        feedback.textContent = "Please wait. The first analysis can take longer while models load.";
    });
    window.addEventListener("pageshow", () => {
        submitting = false; remove.disabled = false; form.removeAttribute("aria-busy");
        button.querySelector("span").textContent = "Detect crop & analyze";
        button.querySelector("i").className = "bi bi-stars";
        showFile();
    });
});
