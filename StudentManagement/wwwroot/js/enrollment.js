function saveEnrollment(event) {
    event.preventDefault();

    // The API binds these as numbers; empty inputs must be sent as null ("" fails decimal? binding).
    var id = $('#Id').length ? Number($('#Id').val()) || 0 : 0;
    var studentId = $('#studentId').length && $('#studentId').val() !== '' ? Number($('#studentId').val()) : null;
    var courseId = $('#courseId').length && $('#courseId').val() !== '' ? Number($('#courseId').val()) : null;
    var grade = $('#grade').length && $('#grade').val() !== '' ? Number($('#grade').val()) : null;

    if (!studentId || !courseId) {
        alert('Please select both student and course.');
        return;
    }

    var enrollment = {
        Id: id,
        StudentId: studentId,
        CourseId: courseId,
        Grade: grade
    };

    $.ajax({
        url: '/Enrollment/Save',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(enrollment),
        success: function (res) {
            if (res) {
                window.location.href = '/Enrollment/Index';
            } else {
                alert('Failed to save enrollment.');
            }
        },
        error: function (xhr, status, error) {
            console.error(error);
            alert('AJAX error: ' + error);
        }
    });
}
