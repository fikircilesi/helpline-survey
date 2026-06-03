"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.stopLoading = exports.getState = exports.onAction = exports.openModal = void 0;
var utils_1 = require("./utils");
var buttons_1 = require("./options/buttons");
var class_list_1 = require("./class-list");
var OVERLAY = class_list_1.default.OVERLAY, SHOW_MODAL = class_list_1.default.SHOW_MODAL, BUTTON = class_list_1.default.BUTTON, BUTTON_LOADING = class_list_1.default.BUTTON_LOADING;
var state_1 = require("./state");
var openModal = function () {
    var overlay = (0, utils_1.getNode)(OVERLAY);
    overlay.classList.add(SHOW_MODAL);
    state_1.default.isOpen = true;
};
exports.openModal = openModal;
var hideModal = function () {
    var overlay = (0, utils_1.getNode)(OVERLAY);
    overlay.classList.remove(SHOW_MODAL);
    state_1.default.isOpen = false;
};
/*
 * Triggers when the user presses any button, or
 * hits Enter inside the input:
 */
var onAction = function (namespace) {
    if (namespace === void 0) { namespace = buttons_1.CANCEL_KEY; }
    var _a = state_1.default.actions[namespace], value = _a.value, closeModal = _a.closeModal;
    if (closeModal === false) {
        var buttonClass = "".concat(BUTTON, "--").concat(namespace);
        var button = (0, utils_1.getNode)(buttonClass);
        button.classList.add(BUTTON_LOADING);
    }
    else {
        hideModal();
    }
    state_1.default.promise.resolve(value);
};
exports.onAction = onAction;
/*
 * Filter the state object. Remove the stuff
 * that's only for internal use
 */
var getState = function () {
    var publicState = Object.assign({}, state_1.default);
    delete publicState.promise;
    delete publicState.timer;
    return publicState;
};
exports.getState = getState;
/*
 * Stop showing loading animation on button
 * (to display error message in input for example)
 */
var stopLoading = function () {
    var buttons = document.querySelectorAll(".".concat(BUTTON));
    for (var i = 0; i < buttons.length; i++) {
        var button = buttons[i];
        button.classList.remove(BUTTON_LOADING);
    }
};
exports.stopLoading = stopLoading;
//# sourceMappingURL=actions.js.map