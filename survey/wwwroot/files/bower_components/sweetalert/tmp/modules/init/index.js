"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.init = void 0;
var utils_1 = require("../utils");
var class_list_1 = require("../class-list");
var MODAL = class_list_1.default.MODAL;
var modal_1 = require("./modal");
var overlay_1 = require("./overlay");
var event_listeners_1 = require("../event-listeners");
var utils_2 = require("../utils");
/*
 * Inject modal and overlay into the DOM
 * Then format the modal according to the given opts
 */
var init = function (opts) {
    var modal = (0, utils_1.getNode)(MODAL);
    if (!modal) {
        if (!document.body) {
            (0, utils_2.throwErr)("You can only use SweetAlert AFTER the DOM has loaded!");
        }
        (0, overlay_1.default)();
        (0, modal_1.default)();
    }
    (0, modal_1.initModalContent)(opts);
    (0, event_listeners_1.default)(opts);
};
exports.init = init;
exports.default = exports.init;
//# sourceMappingURL=index.js.map