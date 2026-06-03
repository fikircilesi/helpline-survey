"use strict";
/*
 * List of class names that we
 * use throughout the library to
 * manipulate the DOM.
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.CLASS_NAMES = void 0;
;
var OVERLAY = 'swal-overlay';
var BUTTON = 'swal-button';
var ICON = 'swal-icon';
exports.CLASS_NAMES = {
    MODAL: 'swal-modal',
    OVERLAY: OVERLAY,
    SHOW_MODAL: "".concat(OVERLAY, "--show-modal"),
    MODAL_TITLE: "swal-title",
    MODAL_TEXT: "swal-text",
    ICON: ICON,
    ICON_CUSTOM: "".concat(ICON, "--custom"),
    CONTENT: 'swal-content',
    FOOTER: 'swal-footer',
    BUTTON_CONTAINER: 'swal-button-container',
    BUTTON: BUTTON,
    CONFIRM_BUTTON: "".concat(BUTTON, "--confirm"),
    CANCEL_BUTTON: "".concat(BUTTON, "--cancel"),
    DANGER_BUTTON: "".concat(BUTTON, "--danger"),
    BUTTON_LOADING: "".concat(BUTTON, "--loading"),
    BUTTON_LOADER: "".concat(BUTTON, "__loader"),
};
exports.default = exports.CLASS_NAMES;
//# sourceMappingURL=index.js.map