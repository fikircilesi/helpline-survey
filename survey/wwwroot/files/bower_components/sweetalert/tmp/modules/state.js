"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.setActionOptionsFor = exports.setActionValue = exports.resetState = void 0;
var buttons_1 = require("./options/buttons");
;
;
var defaultState = {
    isOpen: false,
    promise: null,
    actions: {},
    timer: null,
};
var state = Object.assign({}, defaultState);
var resetState = function () {
    state = Object.assign({}, defaultState);
};
exports.resetState = resetState;
/*
 * Change what the promise resolves to when the user clicks the button.
 * This is called internally when using { input: true } for example.
 */
var setActionValue = function (opts) {
    if (typeof opts === "string") {
        return setActionValueForButton(buttons_1.CONFIRM_KEY, opts);
    }
    for (var namespace in opts) {
        setActionValueForButton(namespace, opts[namespace]);
    }
};
exports.setActionValue = setActionValue;
var setActionValueForButton = function (namespace, value) {
    if (!state.actions[namespace]) {
        state.actions[namespace] = {};
    }
    Object.assign(state.actions[namespace], {
        value: value,
    });
};
/*
 * Sets other button options, e.g.
 * whether the button should close the modal or not
 */
var setActionOptionsFor = function (buttonKey, _a) {
    var _b = _a === void 0 ? {} : _a, _c = _b.closeModal, closeModal = _c === void 0 ? true : _c;
    Object.assign(state.actions[buttonKey], {
        closeModal: closeModal,
    });
};
exports.setActionOptionsFor = setActionOptionsFor;
exports.default = state;
//# sourceMappingURL=state.js.map