# C# Assessment app

An application for C# (.net) knowledge assessment

## Description

This is a web application that interacts with a 3nd party service (<https://fakeapi.platzi.com>/<https://api.escuelajs.co>) and serves data

We have to do some code refactoring and implement some new features

## Code refactoring

Seems that the use of http client is not so much efficient

Let's make a different, more solid, approach/implementation

Feel free to justify and do any changes you consider necessary to improve the code quality and performance

## New features

**#1**

Right now only the **getAll** method supported for **products**

We have to implement **getOne** and **create** methods also

**#2**

Add implementation for **categories**

**#3**

3nd party service supports JWT auth. We have to implement and support it. Use the credentials provided to appsettings.json file.

**#4**

We must measure and log the performance of the requests. Create a middleware to achieve this.

## Implementation

* Try to understand and keep the architectural approach.
* Add unit testing.
* Add integration testing.
* Add docker support.
* Using CQRS pattern will be considered as a strong plus.
* Add health check for the api and self.
* The attached collections (postman/insomnia) will help you with the requests.
* Feel Free to use any AI agent but describe which areas where created by it and why.
* Add a README.md file with instructions to run the application and tests.
* The application must be implemented in C# using .NET 10 and take advantage of its features.
* Explain your design decisions and the reasons behind them in the README.md file.
* Εxtra points will be given for raising security and performance concerns and providing solutions to them.
* The application must be implemented in a clean and maintainable way, following SOLID principles and best practices.
* The application must be implemented in a way that is easy to understand and maintain, with clear separation of concerns and a well-defined architecture.
* The application must be implemented in a way that is easy to test, with a focus on unit testing and integration testing.
* Do as much possible in the application to make it production-ready, including error handling, logging, and monitoring. in the 4-6hrs time Frame.
* Suggest any improvements or new features that could be added to the application in the future.
