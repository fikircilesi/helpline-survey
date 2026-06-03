"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var jquery_1 = require("jquery");
var func_1 = require("../core/func");
var lists_1 = require("../core/lists");
var AIR_MODE_POPOVER_X_OFFSET = 20;
var AirPopover = /** @class */ (function () {
    function AirPopover(context) {
        var _this = this;
        this.context = context;
        this.ui = jquery_1.default.summernote.ui;
        this.options = context.options;
        this.hidable = true;
        this.events = {
            'summernote.keyup summernote.mouseup summernote.scroll': function () {
                if (_this.options.editing) {
                    _this.update();
                }
            },
            'summernote.disable summernote.change summernote.dialog.shown summernote.blur': function () {
                _this.hide();
            },
            'summernote.focusout': function (we, e) {
                if (!_this.$popover.is(':active,:focus')) {
                    _this.hide();
                }
            },
        };
    }
    AirPopover.prototype.shouldInitialize = function () {
        return this.options.airMode && !lists_1.default.isEmpty(this.options.popover.air);
    };
    AirPopover.prototype.initialize = function () {
        var _this = this;
        this.$popover = this.ui.popover({
            className: 'note-air-popover',
        }).render().appendTo(this.options.container);
        var $content = this.$popover.find('.popover-content');
        this.context.invoke('buttons.build', $content, this.options.popover.air);
        // disable hiding this popover preemptively by 'summernote.blur' event.
        this.$popover.on('mousedown', function () { _this.hidable = false; });
        // (re-)enable hiding after 'summernote.blur' has been handled (aka. ignored).
        this.$popover.on('mouseup', function () { _this.hidable = true; });
    };
    AirPopover.prototype.destroy = function () {
        this.$popover.remove();
    };
    AirPopover.prototype.update = function () {
        var styleInfo = this.context.invoke('editor.currentStyle');
        if (styleInfo.range && !styleInfo.range.isCollapsed()) {
            var rect = lists_1.default.last(styleInfo.range.getClientRects());
            if (rect) {
                var bnd = func_1.default.rect2bnd(rect);
                this.$popover.css({
                    display: 'block',
                    left: Math.max(bnd.left + bnd.width / 2, 0) - AIR_MODE_POPOVER_X_OFFSET,
                    top: bnd.top + bnd.height,
                });
                this.context.invoke('buttons.updateCurrentStyle', this.$popover);
            }
        }
        else {
            this.hide();
        }
    };
    AirPopover.prototype.hide = function () {
        if (this.hidable) {
            this.$popover.hide();
        }
    };
    return AirPopover;
}());
exports.default = AirPopover;
//# sourceMappingURL=AirPopover.js.map