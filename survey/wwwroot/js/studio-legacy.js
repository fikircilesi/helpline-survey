(function () {
    function ensureAvatarViewModal() {
        var existing = document.querySelector("[data-avatar-view-modal]");
        if (existing) {
            return existing;
        }

        var modal = document.createElement("div");
        modal.className = "avatar-view-modal";
        modal.hidden = true;
        modal.setAttribute("data-avatar-view-modal", "true");
        modal.innerHTML =
            '<div class="avatar-view-backdrop" data-avatar-view-close></div>' +
            '<div class="avatar-view-dialog" role="dialog" aria-modal="true" aria-labelledby="avatar-view-title">' +
            '  <div class="avatar-view-head">' +
            '    <div><span>Profil Görüntüsü</span><h3 id="avatar-view-title" data-avatar-view-name>Profil</h3></div>' +
            '    <button type="button" class="avatar-view-close" data-avatar-view-close title="Kapat"><i class="fa fa-times"></i></button>' +
            '  </div>' +
            '  <div class="avatar-view-frame is-neutral" data-avatar-view-frame>' +
            '    <img alt="" data-avatar-view-img hidden />' +
            '    <span class="avatar-view-fallback"><i class="fa fa-user" aria-hidden="true" data-avatar-view-icon></i></span>' +
            '  </div>' +
            '  <p class="avatar-view-note" data-avatar-view-note>Profil fotoğrafı veya avatar büyük görünümde açıldı.</p>' +
            '</div>';

        document.body.appendChild(modal);
        return modal;
    }

    function avatarDataFromElement(element) {
        if (!element) {
            return null;
        }

        var image = element.querySelector ? element.querySelector("img") : null;
        var src = element.dataset.avatarSrc || (image && (image.currentSrc || image.src)) || "";
        var hasPhoto = !!src && element.classList && element.classList.contains("has-photo");
        return {
            name: element.dataset.avatarName || element.getAttribute("aria-label") || element.getAttribute("title") || "Profil",
            src: hasPhoto ? src : "",
            tone: element.dataset.avatarTone || "is-neutral",
            icon: element.dataset.avatarIcon || "fa-user"
        };
    }

    function applyAvatarViewData(modal, data) {
        var frame = modal.querySelector("[data-avatar-view-frame]");
        var image = modal.querySelector("[data-avatar-view-img]");
        var icon = modal.querySelector("[data-avatar-view-icon]");
        var name = modal.querySelector("[data-avatar-view-name]");
        var note = modal.querySelector("[data-avatar-view-note]");

        if (!frame || !data) {
            return;
        }

        frame.classList.remove("is-male", "is-female", "is-neutral", "has-photo");
        frame.classList.add(data.tone || "is-neutral");

        if (image) {
            image.hidden = !data.src;
            if (data.src) {
                image.src = data.src;
            } else {
                image.removeAttribute("src");
            }
            image.alt = data.name || "";
            image.onerror = function () {
                image.hidden = true;
                image.removeAttribute("src");
                frame.classList.remove("has-photo");
                if (note) {
                    note.textContent = "Fotoğraf bulunamadı; avatar gösteriliyor.";
                }
            };
        }

        if (data.src) {
            frame.classList.add("has-photo");
        }

        if (icon) {
            icon.className = "fa " + (data.icon || "fa-user");
        }
        if (name) {
            name.textContent = data.name || "Profil";
        }
        if (note) {
            note.textContent = data.src
                ? "Mevcut fotoğraf büyük görünümde açıldı."
                : "Bu kayıt özel fotoğraf yerine avatar kullanıyor.";
        }
    }

    function openAvatarView(data) {
        var modal = ensureAvatarViewModal();
        applyAvatarViewData(modal, data);
        modal.hidden = false;
        document.body.classList.add("avatar-view-open");
    }

    function closeAvatarView(modal) {
        if (!modal) {
            return;
        }

        modal.hidden = true;
        document.body.classList.remove("avatar-view-open");
    }

    window.aslanaAvatarPreviewOpen = openAvatarView;
    window.aslanaAvatarDataFromElement = avatarDataFromElement;

    document.addEventListener("click", function (event) {
        var closer = event.target.closest("[data-avatar-view-close]");
        if (closer) {
            closeAvatarView(closer.closest("[data-avatar-view-modal]"));
            return;
        }

        if (event.target.closest("[data-quick-photo-open]")) {
            return;
        }

        var opener = event.target.closest("[data-avatar-preview-open]");
        if (!opener) {
            return;
        }

        event.preventDefault();
        openAvatarView(avatarDataFromElement(opener));
    });

    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape") {
            return;
        }

        document.querySelectorAll("[data-avatar-view-modal]:not([hidden])").forEach(closeAvatarView);
    });

    function normalize(text) {
        return (text || "").toLocaleLowerCase("tr-TR").trim();
    }

    function looksLikeDelete(text) {
        var value = normalize(text);
        return value.indexOf("sil") > -1 || value.indexOf("delete") > -1;
    }

    function enhanceDeletePanels() {
        document.querySelectorAll(".panel").forEach(function (panel) {
            var heading = panel.querySelector(".panel-heading, h4");
            var sourceText = heading ? heading.textContent : "";

            if (looksLikeDelete(sourceText)) {
                panel.classList.add("studio-delete-context");
            }
        });

        document.querySelectorAll('input[type="submit"], button[type="submit"]').forEach(function (button) {
            var label = button.value || button.textContent || "";

            if (looksLikeDelete(label)) {
                button.classList.add("studio-danger-submit");
            }
        });
    }

    function enhanceTables() {
        document.querySelectorAll(".panel-body > table.table").forEach(function (table) {
            if (table.parentElement && table.parentElement.classList.contains("studio-table-wrap")) {
                return;
            }

            var wrapper = document.createElement("div");
            wrapper.className = "studio-table-wrap";
            table.parentNode.insertBefore(wrapper, table);
            wrapper.appendChild(table);
        });
    }

    document.addEventListener("DOMContentLoaded", function () {
        enhanceDeletePanels();
        enhanceTables();
    });
})();
