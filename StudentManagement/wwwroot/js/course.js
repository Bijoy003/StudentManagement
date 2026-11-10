function saveCourse(event) {
    event.preventDefault();

    var course = {
        Id: $('#Id').val() || 0,
        Name: $('#name').val(),
        Credits: $('#credits').val(),
        GradeMode: parseInt($('#grade-mode').val(), 10) || 0
    };

    $.ajax({
        url: '/Course/Save',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(course),
        success: function (res) {
            if (res) {
                window.location.href = '/Course/Index';
            } else {
                alert('Failed to save course.');
            }
        }
    });
}
