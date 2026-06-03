  'use strict';
$(document).ready(function () {
	function bindClick(selector, handler) {
		var element = document.querySelector(selector);
		if (element) {
			element.onclick = handler;
		}
	}

//Basic alert
	bindClick('.sweet-1', function(){
		swal("Here's a message!", "It's pretty, isn't it?")
	});
	//success message
	bindClick('.alert-success-msg', function(){
		swal("Good job!", "You clicked the button!", "success");
	});

	//Alert confirm
	bindClick('.alert-confirm', function(){
		swal({
					title: "Are you sure?",
					text: "Your will not be able to recover this imaginary file!",
					type: "warning",
					showCancelButton: true,
					confirmButtonClass: "btn-danger",
					confirmButtonText: "Yes, delete it!",
					closeOnConfirm: false
				},
				function(){
					swal("Deleted!", "Your imaginary file has been deleted.", "success");
				});
	});

	//Success or cancel alert
	bindClick('.alert-success-cancel', function(){
		swal({
					title: "Are you sure?",
					text: "You will not be able to recover this imaginary file!",
					type: "warning",
					showCancelButton: true,
					confirmButtonClass: "btn-danger",
					confirmButtonText: "Yes, delete it!",
					cancelButtonText: "No, cancel plx!",
					closeOnConfirm: false,
					closeOnCancel: false
				},
				function(isConfirm) {
					if (isConfirm) {
						swal("Deleted!", "Your imaginary file has been deleted.", "success");
					} else {
						swal("Cancelled", "Your imaginary file is safe :)", "error");
					}
				});
	});
	//prompt alert
	bindClick('.alert-prompt', function(){
		swal({
			title: "An input!",
			text: "Write something interesting:",
			type: "input",
			showCancelButton: true,
			closeOnConfirm: false,
			inputPlaceholder: "Write something"
		}, function (inputValue) {
			if (inputValue === false) return false;
			if (inputValue === "") {
				swal.showInputError("You need to write something!");
				return false
			}
			swal("Nice!", "You wrote: " + inputValue, "success");
		});
	});

	//Ajax alert
	bindClick('.alert-ajax', function(){
		swal({
			title: "Ajax request example",
			text: "Submit to run ajax request",
			type: "info",
			showCancelButton: true,
			closeOnConfirm: false,
			showLoaderOnConfirm: true
		}, function () {
			setTimeout(function () {
				swal("Ajax request finished!");
			}, 2000);
		});
	});

		$('#openBtn').on('click',function () {
			var modal = $('#myModal');
			if (modal.length && $.fn.modal) {
				modal.modal({
					show: true
				})
			}
		});

		$(document).on('show.bs.modal', '.modal', function () {
			var zIndex = 1040 + (10 * $('.modal:visible').length);
			$(this).css('z-index', zIndex);
			setTimeout(function() {
				$('.modal-backdrop').not('.modal-stack').css('z-index', zIndex - 1).addClass('modal-stack');
			}, 0);
		});
	});
  
