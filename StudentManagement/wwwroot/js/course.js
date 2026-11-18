function saveCourse(event) {
    event.preventDefault();

    var maxEnrollments = $('#max-enrollments').val();
    var course = {
        Id: $('#Id').val() || 0,
        Name: $('#name').val(),
        Credits: $('#credits').val(),
        GradeMode: parseInt($('#grade-mode').val(), 10) || 0,
        MaxEnrollments: maxEnrollments === '' || maxEnrollments === undefined ? null : parseInt(maxEnrollments, 10)
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
        },
        error: function (xhr) {
            alert(xhr.responseJSON && xhr.responseJSON.message ? xhr.responseJSON.message : 'Failed to save course.');
        }
    });
}
