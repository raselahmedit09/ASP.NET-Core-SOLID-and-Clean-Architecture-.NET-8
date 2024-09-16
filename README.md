# HR Management System with Clean Architecture and API Gateway

This repository contains a microservices-based HR Management System, built with .NET 8, following Clean Architecture principles and utilizing modern development patterns like CQRS and MediatR. The project consists of three APIs and uses an Ocelot API Gateway for routing and inter-service communication. NSwag is integrated to generate client methods for seamless API-to-API calls.

## Features

- Clean Architecture: Follows the Clean Architecture pattern to ensure a clear separation of layers (Domain, Application, Infrastructure, and API).
CQRS (Command Query Responsibility Segregation): Implements the CQRS pattern to separate read and write operations, improving performance and scalability.
- MediatR: Uses MediatR to handle commands and queries, promoting decoupling between components.
- AutoMapper: For object-to-object mapping, simplifying the transformation of data between layers.
- Fluent Validation: Provides a clear and concise way to implement validation rules for models.
- Entity Framework Core: An ORM for database operations, providing an abstraction layer over database access and allowing for efficient querying and updates.
- Global Exception Handling: Implements a consistent global exception handling strategy across the application.
- Ocelot API Gateway: Acts as the gateway for handling all incoming API requests, providing routing, request aggregation, and security.
- NSwag: Generates C# client code for making API-to-API calls based on OpenAPI specifications, simplifying integration between microservices.

## Key Functionalities

- API-to-API Communication via API Gateway: This project uses the Ocelot API Gateway to route requests between APIs, allowing for seamless communication and load balancing between services.
- NSwag Code Generation: Using NSwag, the project automatically generates API client methods, simplifying the process of making API-to-API calls. These client methods are leveraged to call other microservices in a clean, maintainable way.

## Getting Started

To get started with this project, ensure that you have the following prerequisites installed:

- .NET 8 SDK
- SQL Server or a compatible database
- Ocelot API Gateway
- NSwag

## How to Run

- git clone https://github.com/raselahmedit09/ASP.NET-Core-SOLID-and-Clean-Architecture-.NET-8.git
or Download zip file and unzip the project. 
- Click Properties from the solution :
![HR Management System](https://github.com/user-attachments/assets/49d662f1-13bd-4635-9a07-df6dddd7d87f)

![image](https://github.com/user-attachments/assets/09680276-9b64-4c73-be1e-f6efe6e26801)



## NSwag Client Generation

NSwag is configured to automatically generate client methods for API calls. You can modify the nswag.json configuration file for custom settings or regenerate the client 

## API Gateway Configuration

The Ocelot API Gateway is configured to route requests between different services. The gateway routes are defined in the ocelot.json file.

## Technologies Used

- .NET 8
- MediatR
- CQRS
- Entity Framework Core
- FluentValidation
- AutoMapper
- Ocelot API Gateway
- NSwag (for OpenAPI to C# client generation)

## Database
```sh
USE [db_hr_leavemanagement]
GO

/****** Object:  Table [dbo].[LeaveTypes]    Script Date: 9/16/2024 9:30:16 AM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[LeaveTypes](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](100) NOT NULL,
	[DefaultDays] [int] NOT NULL,
	[DateCreated] [datetime2](7) NULL,
	[DateModified] [datetime2](7) NULL,
 CONSTRAINT [PK_LeaveTypes] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
```

```sh
USE [db_hr_leavemanagement]
GO

/****** Object:  Table [dbo].[LeaveRequests]    Script Date: 9/16/2024 9:33:40 AM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[LeaveRequests](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[StartDate] [datetime2](7) NOT NULL,
	[EndDate] [datetime2](7) NOT NULL,
	[DateRequested] [datetime2](7) NOT NULL,
	[RequestComments] [nvarchar](max) NULL,
	[Approved] [bit] NULL,
	[Cancelled] [bit] NOT NULL,
	[RequestingEmployeeId] [nvarchar](max) NOT NULL,
	[LeaveTypeId] [int] NOT NULL,
	[DateCreated] [datetime2](7) NULL,
	[DateModified] [datetime2](7) NULL,
 CONSTRAINT [PK_LeaveRequests] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[LeaveRequests]  WITH CHECK ADD  CONSTRAINT [FK_LeaveRequests_LeaveTypes_LeaveTypeId] FOREIGN KEY([LeaveTypeId])
REFERENCES [dbo].[LeaveTypes] ([Id])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[LeaveRequests] CHECK CONSTRAINT [FK_LeaveRequests_LeaveTypes_LeaveTypeId]
GO
```

```sh

USE [db_hr_leavemanagement]
GO

/****** Object:  Table [dbo].[LeaveAllocations]    Script Date: 9/16/2024 9:18:58 AM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[LeaveAllocations](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[NumberOfDays] [int] NOT NULL,
	[Period] [int] NOT NULL,
	[LeaveTypeId] [int] NOT NULL,
	[DateCreated] [datetime2](7) NULL,
	[DateModified] [datetime2](7) NULL,
	[EmployeeId] [nvarchar](max) NOT NULL,
 CONSTRAINT [PK_LeaveAllocations] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[LeaveAllocations] ADD  DEFAULT (N'') FOR [EmployeeId]
GO

ALTER TABLE [dbo].[LeaveAllocations]  WITH CHECK ADD  CONSTRAINT [FK_LeaveAllocations_LeaveTypes_LeaveTypeId] FOREIGN KEY([LeaveTypeId])
REFERENCES [dbo].[LeaveTypes] ([Id])
ON DELETE CASCADE
GO

ALTER TABLE [dbo].[LeaveAllocations] CHECK CONSTRAINT [FK_LeaveAllocations_LeaveTypes_LeaveTypeId]
GO
```
