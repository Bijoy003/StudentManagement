# Student Management Application Overview

Student Management is an ASP.NET Core MVC web application for managing students, courses, and enrollments at a school or training organization.

## Authentication and roles

- Users must log in to access Students, Courses, Enrollments, and Chat.
- Login supports local accounts (email/password) and Google OAuth.
- The Admin role can create new users and assign roles from the Users page.
- Account features include profile management, password change, forgot/reset password (via email), and optional two-factor authentication (TOTP).

## Main navigation (after login)

- Home — landing page
- My Profile — view and update account details
- Users — admin-only user creation
- Students — student grid (Admin and Teacher); create/edit/delete and the EnrolledInMoreThan report are admin-only
- Courses — course catalog and reports; staff manage courses, students self-enroll from here
- Enrollments — staff link students to courses and view relationships
- My Courses — Student role: view and drop own enrollments (enrolling happens in the Courses catalog; the account must be linked to a student record by an admin)
- Chat — AI assistant for app help and live data questions

## Data model summary

- Student: Id, Name, Email, Phone, Address, DateOfEnroll
- Course: Id, Name, Credits, optional admin-set Maximum Enrollments (seat cap; empty = unlimited)
- Enrollment: links a Student to a Course with an optional Grade

## Important behavior

- The chat assistant can fetch live student, course, and enrollment data using tools. Admin and Teacher accounts get the full tool set; Student and User accounts only get the course catalog. The assistant should not invent counts or records.
- Write operations (create, update, delete) are performed through the web UI, not through chat.
