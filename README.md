Student Management System

An ASP.NET Core web application designed to simplify student administration through automated class placement, student record management, academic progression, financial tracking, and voice-activated reporting.

Project Overview

The Student Management System is a locally operated web application developed to streamline administrative processes within a school environment.

The application combines student record management with automated workflows to reduce repetitive administrative tasks, improve data organisation, and provide easier access to important academic and financial information.

It is built using C# and ASP.NET Core, with Firebase providing backend data persistence.

Key Features

1. Smart Class List Import and Automated Class Placement

Imports and extracts student information from class lists.

Identifies student records and relevant details, including names and ages.

Uses duplicate detection checks, including student names and dates of birth, to reduce duplicate records.

Automatically places students into their appropriate classes within the database.

2. Student Enrollment and Academic Progression

Manages student enrollment and ongoing student records.

Supports student information management throughout their academic journey.

Automates grade-level promotions across academic terms and school years.

3. Fee and Payment Management

Tracks student financial obligations and payments.

Uses a cumulative balance model to maintain financial records across multiple terms.

Provides a clearer overview of outstanding balances and payment progress.

4. Voice-Activated Commands and PDF Analytics

Provides a voice-command interface for interacting with application functionality.

Supports natural-language reporting workflows.

Allows users to trigger actions through spoken commands, including exporting analytics reports directly to PDF.

Combines voice interaction with reporting functionality to make administrative tasks more accessible.

Technology Stack

Category

Technologies

Programming Language

C#

Framework

ASP.NET Core

Architecture

Model-View-Controller (MVC)

Frontend

HTML, CSS, JavaScript

Backend and Data Persistence

Firebase

Reporting

PDF analytics export

Voice Interaction

JavaScript-based voice command functionality

Development Environment

Visual Studio

Architecture and Design

The application follows the Model-View-Controller (MVC) architectural pattern, separating application data, user interface components, and request-handling logic.

Its structure includes:

Models: Represent application data and student-related entities.

Views and Pages: Provide the user interface and administrative workflows.

Controllers: Handle application requests and coordinate application functionality.

Services: Encapsulate reusable application logic and shared resources.

Firebase Integration: Supports persistent storage of student records, class assignments, and financial information.

The application also incorporates design patterns such as Singleton for appropriate shared resources.

Repository Structure

Student-Management-System/
│
├── .github/
│
├── StudentManagement/
│   ├── Controllers/
│   ├── Models/
│   ├── Pages/
│   ├── Services/
│   ├── wwwroot/
│   ├── Program.cs
│   ├── StudentManagement.csproj
│   └── watch.json
│
├── .gitattributes
├── .gitignore
└── README.md

Running the Application Locally

The application is designed to run as a local web application.

Prerequisites

Visual Studio with ASP.NET Core development support.

A compatible .NET SDK.

Access to the Firebase project used by the application.

Git (optional, for cloning the repository).

Setup

Clone the repository:

git clone https://github.com/Omotayo-ini/Student-Management-System.git

Open the solution or project in Visual Studio.

Configure the required Firebase settings and application configuration locally.

Restore the project's dependencies.

Build the application and resolve any environment-specific configuration issues.

Run the application using Visual Studio or the .NET CLI:

dotnet run --project StudentManagement/StudentManagement.csproj

Configuration note: Application settings and Firebase credentials are environment-specific and should be configured locally. Private service account credentials must never be committed to the repository.

Development Status

Current status: Active development and testing

The application's primary functionality has been implemented and is working during basic testing.

Current development efforts focus on extended testing of automated workflows, including scenarios involving different academic periods, student progression, class placement, and financial calculations.

The objective is to verify that automated processes behave consistently across different scenarios and time periods before considering the application fully tested.

Project Goals

The project aims to:

Reduce manual administrative work within school environments.

Improve the organisation and accessibility of student information.

Minimise duplicate student records.

Simplify academic progression and financial tracking.

Introduce voice-assisted interaction into routine administrative workflows.

Make school analytics easier to access through automated PDF reporting.

Application Screenshots

1. Dashboard

The main dashboard provides an overview of the application's student management functionality.



2. Student Records

The student records interface supports the management and organisation of student information.



3. Automated Processes

The application incorporates automated workflows for student administration and academic progression.



4. Data Import and Class Placement

The data import interface supports the extraction, processing, and organisation of student records from class lists.



5. Analytics and Voice Assistant

The analytics and voice assistant functionality provides voice-activated interactions and reporting capabilities, including PDF exports.
Author

Inioluwa Omotayo

GitHub: @Omotayo-ini

Developed as a practical software engineering project focused on automation, data management, and improving school administration through technology.
