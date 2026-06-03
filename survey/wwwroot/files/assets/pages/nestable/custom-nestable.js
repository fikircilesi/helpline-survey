  'use strict';
  $(document).ready(function() {

      var updateOutput = function(e) {
          var list = e.length ? e : $(e.target),
              output = list.data('output');
          if (!list.length || !output || !output.length) {
              return;
          }
          if (window.JSON) {
              output.val(window.JSON.stringify(list.nestable('serialize'))); //, null, 2));
          } else {
              output.val('JSON browser support required for this demo.');
          }
      };

      // activate Nestable for list 1
      if ($('#nestable').length && $.fn.nestable) {
      $('#nestable').nestable({
              group: 1
          })
          .on('change', updateOutput);
      }

      // activate Nestable for list 2
      if ($('#nestable2').length && $.fn.nestable) {
      $('#nestable2').nestable({
              group: 1
          })
          .on('change', updateOutput);
      }

      // activate Nestable for list 2
      if ($('#color-nestable').length && $.fn.nestable) {
      $('#color-nestable').nestable({
              group: 1
          })
          .on('change', updateOutput);
      }

      // output initial serialised data
      updateOutput($('#nestable').data('output', $('#nestable-output')));
      updateOutput($('#nestable2').data('output', $('#nestable2-output')));
      updateOutput($('#color-nestable').data('output', $('#color-nestable-output')));

      $('#nestable-menu').on('click', function(e) {
          var target = $(e.target),
              action = target.data('action');
          if (action === 'expand-all') {
              $('.dd').nestable('expandAll');
          }
          if (action === 'collapse-all') {
              $('.dd').nestable('collapseAll');

          }
      });

      if ($('#nestable3').length && $.fn.nestable) {
          $('#nestable3').nestable();
      }

  });
