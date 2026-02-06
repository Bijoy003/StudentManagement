let selectedStudentId = null;
let studentDataTable = null;

$(document).ready(async function () {
    if (document.getElementById("date-of-enroll") !== null) {
        var date = new Date().toISOString().slice(0, 16);
        if ($("#temp-date-of-enroll").val() !== null && $("#temp-date-of-enroll").val() !== undefined) {
            date = new Date($("#temp-date-of-enroll").val()).toISOString().slice(0, 16);
        }

        document.getElementById("date-of-enroll").value = date;
    }

    await loadStudents();

    $('#student-grid tbody')
        .off() //turn off the event before turning it on to prevent firing click event multiple times
        .on('click', 'td', async function () {
            let cellIndex = $(this).index();

            rowData = studentDataTable.row($(this).closest('tr')).data();
            selectedStudentId = rowData.id;

            const isClickedRowSelected = $(this).closest('tr').hasClass('selected-grid-row');

            if (isClickedRowSelected) {
                $(this).closest('tr').removeClass('selected-grid-row');
                selectedStudentId = null;
            }
            else {
                const anyRowHasClass = $('#student-grid tbody tr.selected-grid-row').length > 0;

                if (anyRowHasClass) {
                    $('#student-grid tbody tr.selected-grid-row').removeClass('selected-grid-row');
                }

                $(this).closest('tr').addClass('selected-grid-row');
            }
        });
});

async function loadStudents() {
    const url = "/Student/GetStudentList";
    const students = await ajaxGetAsync(url);

    if (studentDataTable) {
        studentDataTable.state.clear();
        studentDataTable.clear();
        studentDataTable.destroy();
    }

    studentDataTable = $('#student-grid').DataTable({
        data: students, // Provide your data array here
        columns: [
            { data: 'id', className: 'dt-center' },
            { data: 'name', className: 'dt-center' },
            { data: 'email', className: 'dt-center' },
            { data: 'phone', className: 'dt-center' },
            { data: 'address', className: 'dt-center' },
            { data: 'dateOfEnroll', className: 'dt-center' }
        ],
        paging: false, // Disable pagination
        searching: false // Disable search
    });
}

function addStudent() {
    const url = "/Student/Create";
    safelyNavigateTo(url);
}

function cancelButtonClicked() {
    const url = "/Student/Index";
    safelyNavigateTo(url);
}

function saveStudent(event) {
    event.preventDefault();

    const student = serializeFormData();
    if (!verifyStudent(student)) {
        return;
    }

    const tempStudentId = $("#temp-student-id");
    if (tempStudentId.length > 0) {
        student.Id = tempStudentId.val();
    }

    const url = `/Student/SaveStudent`;

    ajaxPostAsync(url, student)
        .then((response) => {
            console.log(response)
            if (response === true) {
                const url = `/Student/Index`;
                safelyNavigateTo(url);
            }
        })
        .catch((error) => {
            console.log(error);
            showValidationErrors(error);
        });
}

function serializeFormData() {
    const form = document.getElementById('student-form');
    const formData = new FormData(form);

    for (let [key, value] of formData.entries()) {
        if (value === "") {
            formData.set(key, null);
        }
    }

    const serializedData = Object.fromEntries(formData);
    return serializedData;
}

function safelyNavigateTo(route) {
    window.location.href = route;
}

async function deleteStudent() {
    if (!selectedStudentId) {
        toastr.warning("Plese select a student.");
        return;
    }
    let url = `/Student/DeleteStudent?id=${selectedStudentId}`;
    const students = await ajaxGetAsync(url);
    selectedStudentId = null;
    await loadStudents();
}

function editStudent() {
    if (!selectedStudentId) {
        toastr.warning("Plese select a student.");
        return;
    }
    const url = `/Student/Create?id=${selectedStudentId}`;
    safelyNavigateTo(url);
}

function verifyStudent(student) {
    if (student.Name === "null") {
        toastr.warning("Name can't be empty.");
        return false;
    }
    else if (student.Email === "null") {
        toastr.warning("Email can't be empty.");
        return false;
    }
    else if (student.Address === "null") {
        toastr.warning("Address can't be empty.");
        return false;
    }
    else if (student.Phone === "null") {
        toastr.warning("Phone can't be empty.");
        return false;
    }
    return true;
}

let ajaxGetAsync = async (url, data) => {
    return await $.ajax({
        type: 'GET',
        url: `${url}`,
        dataType: 'json',
        contentType: "application/json; charset=utf-8",
        data: data,
        success: function (response) {
            return response;
        },
        error: function (error) {
            return error;
        }
    });
}

let ajaxPostAsync = async (url, data) => {
    return await $.ajax({
        type: 'POST',
        url: `${url}`,
        dataType: 'json',
        contentType: "application/json; charset=utf-8",
        data: JSON.stringify(data),
        success: function (response) {
            return response;
        },
        error: function (error) {
            return error;
        }
    });
}

function showValidationErrors(error) {
    toastr.warning(error.responseJSON.message);
}
