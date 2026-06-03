"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.getContentOpts = void 0;
var utils_1 = require("../utils");
;
var defaultInputOptions = {
    element: 'input',
    attributes: {
        placeholder: "",
    },
};
var getContentOpts = function (contentParam) {
    var opts = {};
    if ((0, utils_1.isPlainObject)(contentParam)) {
        return Object.assign(opts, contentParam);
    }
    if (contentParam instanceof Element) {
        return {
            element: contentParam,
        };
    }
    if (contentParam === 'input') {
        return defaultInputOptions;
    }
    return null;
};
exports.getContentOpts = getContentOpts;
//# sourceMappingURL=content.js.map