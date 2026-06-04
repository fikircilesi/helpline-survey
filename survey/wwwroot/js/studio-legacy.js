(function () {
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
