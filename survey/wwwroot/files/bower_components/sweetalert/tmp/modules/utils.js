"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.ordinalSuffixOf = exports.isPlainObject = exports.throwErr = exports.removeNode = exports.insertAfter = exports.stringToNode = exports.getNode = void 0;
/*
 * Get a DOM element from a class name:
 */
var getNode = function (className) {
    var selector = ".".concat(className);
    return document.querySelector(selector);
};
exports.getNode = getNode;
var stringToNode = function (html) {
    var wrapper = document.createElement('div');
    wrapper.innerHTML = html.trim();
    return wrapper.firstChild;
};
exports.stringToNode = stringToNode;
var insertAfter = function (newNode, referenceNode) {
    var nextNode = referenceNode.nextSibling;
    var parentNode = referenceNode.parentNode;
    parentNode.insertBefore(newNode, nextNode);
};
exports.insertAfter = insertAfter;
var removeNode = function (node) {
    node.parentElement.removeChild(node);
};
exports.removeNode = removeNode;
var throwErr = function (message) {
    // Remove multiple spaces:
    message = message.replace(/ +(?= )/g, '');
    message = message.trim();
    throw "SweetAlert: ".concat(message);
};
exports.throwErr = throwErr;
/*
 * Match plain objects ({}) but NOT null
 */
var isPlainObject = function (value) {
    if (Object.prototype.toString.call(value) !== '[object Object]') {
        return false;
    }
    else {
        var prototype = Object.getPrototypeOf(value);
        return prototype === null || prototype === Object.prototype;
    }
};
exports.isPlainObject = isPlainObject;
/*
 * Take a number and return a version with ordinal suffix
 * Example: 1 => 1st
 */
var ordinalSuffixOf = function (num) {
    var j = num % 10;
    var k = num % 100;
    if (j === 1 && k !== 11) {
        return "".concat(num, "st");
    }
    if (j === 2 && k !== 12) {
        return "".concat(num, "nd");
    }
    if (j === 3 && k !== 13) {
        return "".concat(num, "rd");
    }
    return "".concat(num, "th");
};
exports.ordinalSuffixOf = ordinalSuffixOf;
//# sourceMappingURL=utils.js.map