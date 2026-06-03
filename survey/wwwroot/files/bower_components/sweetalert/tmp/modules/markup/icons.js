"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.successIconMarkup = exports.warningIconMarkup = exports.errorIconMarkup = void 0;
var class_list_1 = require("../class-list");
var ICON = class_list_1.default.ICON;
var errorIconMarkup = function () {
    var icon = "".concat(ICON, "--error");
    var line = "".concat(icon, "__line");
    var markup = "\n    <div class=\"".concat(icon, "__x-mark\">\n      <span class=\"").concat(line, " ").concat(line, "--left\"></span>\n      <span class=\"").concat(line, " ").concat(line, "--right\"></span>\n    </div>\n  ");
    return markup;
};
exports.errorIconMarkup = errorIconMarkup;
var warningIconMarkup = function () {
    var icon = "".concat(ICON, "--warning");
    return "\n    <span class=\"".concat(icon, "__body\">\n      <span class=\"").concat(icon, "__dot\"></span>\n    </span>\n  ");
};
exports.warningIconMarkup = warningIconMarkup;
var successIconMarkup = function () {
    var icon = "".concat(ICON, "--success");
    return "\n    <span class=\"".concat(icon, "__line ").concat(icon, "__line--long\"></span>\n    <span class=\"").concat(icon, "__line ").concat(icon, "__line--tip\"></span>\n\n    <div class=\"").concat(icon, "__ring\"></div>\n    <div class=\"").concat(icon, "__hide-corners\"></div>\n  ");
};
exports.successIconMarkup = successIconMarkup;
//# sourceMappingURL=icons.js.map