"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __exportStar = (this && this.__exportStar) || function(m, exports) {
    for (var p in m) if (p !== "default" && !Object.prototype.hasOwnProperty.call(exports, p)) __createBinding(exports, m, p);
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.footerMarkup = exports.textMarkup = exports.titleMarkup = exports.iconMarkup = exports.overlayMarkup = void 0;
__exportStar(require("./modal"), exports);
var overlay_1 = require("./overlay");
Object.defineProperty(exports, "overlayMarkup", { enumerable: true, get: function () { return overlay_1.default; } });
__exportStar(require("./icons"), exports);
__exportStar(require("./content"), exports);
__exportStar(require("./buttons"), exports);
var class_list_1 = require("../class-list");
var MODAL_TITLE = class_list_1.default.MODAL_TITLE, MODAL_TEXT = class_list_1.default.MODAL_TEXT, ICON = class_list_1.default.ICON, FOOTER = class_list_1.default.FOOTER;
exports.iconMarkup = "\n  <div class=\"".concat(ICON, "\"></div>");
exports.titleMarkup = "\n  <div class=\"".concat(MODAL_TITLE, "\"></div>\n");
exports.textMarkup = "\n  <div class=\"".concat(MODAL_TEXT, "\"></div>");
exports.footerMarkup = "\n  <div class=\"".concat(FOOTER, "\"></div>\n");
//# sourceMappingURL=index.js.map