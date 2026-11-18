# Courses and Enrollments

## Courses module

Navigate to **Courses** in the top menu.

### Course list and CRUD

- List all courses with ID, Name, and Credits.
- Create, edit, and delete courses from the UI (Admin and Teacher; teachers only their own courses).
- Admins can set **Maximum Enrollments** (a seat cap) on the course form. Leave it empty for unlimited. Teachers cannot set or change the cap.

### Student course catalog

- Students see the same Courses list with an Enrollment column: an **Enroll** button, an **Enrolled** badge for courses they already take, or a **Full** badge when the seat cap is reached.

### Report: Student count per course

- Shows how many students are enrolled in each course.

## Enrollments module

Navigate to **Enrollments** in the top menu (Admin and Teacher).

### Enrollment list and CRUD

- An enrollment links one student to one course.
- Optional Grade can be stored on the enrollment.
- Staff create enrollments for students; saving is rejected with an error when the course has reached its Maximum Enrollments cap.

### Student self-enrollment and drop

- Students (Student role) whose account is linked to a student record can enroll themselves from the Courses catalog and drop their own enrollments from **My Courses**.
- The account link is managed by an admin on the **Users** page; an unlinked account sees a "not linked" notice instead of courses.
- Self-enrollment is blocked when the course is full, and enrolling twice in the same course is rejected.
- Students can only see and drop their own enrollments, never other students'.

### Reports and lookups

- View students enrolled in a specific course.
- View courses a specific student is enrolled in.

## Chat data tools

When users ask about courses or enrollments, the assistant can:

- List all courses or look up a course by ID (any signed-in role)
- Show student count per course (any signed-in role)
- List enrollments (Admin and Teacher only)
- List students in a course, by course ID (Admin and Teacher only)
- List courses for a student, by student ID (Admin and Teacher only)

Always use tool results for live data. Do not guess enrollment counts or student names.
