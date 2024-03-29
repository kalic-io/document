﻿namespace Documents.CQRS.UnitTests.Handlers
{
    using Bogus;
    using Documents.CQRS.Commands;
    using Documents.CQRS.Handlers;
    using Documents.DTO;
    using Documents.DTO.v1;
    using FakeItEasy;
    using FluentAssertions;
    using MedEasy.CQRS.Core.Commands.Results;
    using MedEasy.DAL.EFStore;
    using MedEasy.DAL.Interfaces;
    using MedEasy.IntegrationTests.Core;
    using Optional;
    using System;
    using System.Collections.Generic;
    using System.Linq.Expressions;
    using System.Threading.Tasks;
    using Xunit;
    using Xunit.Abstractions;
    using Xunit.Categories;
    using Documents.DataStore;
    using NodaTime.Testing;
    using NodaTime;

    [Feature(nameof(Documents))]
    [UnitTest]
    public class HandleCreateDocumentInfoCommandTests : IAsyncLifetime, IClassFixture<SqliteEfCoreDatabaseFixture<DocumentsStore>>
    {
        private readonly ITestOutputHelper _outputHelper;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private HandleCreateDocumentInfoCommand _sut;

        public HandleCreateDocumentInfoCommandTests(ITestOutputHelper outputHelper, SqliteEfCoreDatabaseFixture<DocumentsStore> database)
        {
            _outputHelper = outputHelper;

            _unitOfWorkFactory = new EFUnitOfWorkFactory<DocumentsStore>(database.OptionsBuilder.Options, (options) =>
            {
                DocumentsStore context = new(options, new FakeClock(new Instant()));
                context.Database.EnsureCreated();

                return context;
            });

            _unitOfWorkFactory = A.Fake<IUnitOfWorkFactory>(x => x.Wrapping(_unitOfWorkFactory));

            _sut = new HandleCreateDocumentInfoCommand(_unitOfWorkFactory);
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.NewUnitOfWork())
            {
                uow.Repository<Objects.Document>().Clear();

                await uow.SaveChangesAsync()
                    .ConfigureAwait(false);
            }
            _unitOfWorkFactory = null;
            _sut = null;
        }

        public static IEnumerable<object[]> ValidInfoCreateRecordCases
        {
            get
            {
                Faker<NewDocumentInfo> newDocumentFaker = new Faker<NewDocumentInfo>()
                    .RuleFor(x => x.Name, faker => faker.System.FileName())
                    .RuleFor(x => x.MimeType, faker => faker.System.MimeType())
                    .RuleFor(x => x.Content, faker => faker.Hacker.Random.Bytes(10));
                {
                    NewDocumentInfo data = newDocumentFaker.Generate();
                    yield return new object[]
                    {
                        data,
                        (Expression<Func<DocumentInfo, bool>>)(document => document.Id != default
                            && document.Hash != null && document.Hash.Length > 0
                            && document.Name == data.Name
                            && document.MimeType == data.MimeType
                        )
                    };
                }
            }
        }

        [Theory]
        [MemberData(nameof(ValidInfoCreateRecordCases))]
        public async Task GivenValidInfo_Handler_CreatesResource(NewDocumentInfo info, Expression<Func<DocumentInfo, bool>> createdResourceExpectation)
        {
            _outputHelper.WriteLine($"{nameof(info)} : {info}");

            // Arrange
            CreateDocumentInfoCommand cmd = new(info);

            // Act
            Option<DocumentInfo, CreateCommandResult> optionalDocument = await _sut.Handle(cmd, default)
                .ConfigureAwait(false);

            // Assert
            optionalDocument.HasValue.Should()
                .BeTrue();

            optionalDocument.MatchSome(async document =>
            {
                document.Should()
                    .NotBeNull().And
                    .Match(createdResourceExpectation);

                using IUnitOfWork uow = _unitOfWorkFactory.NewUnitOfWork();
                bool createdInDatastore = await uow.Repository<Objects.Document>().AnyAsync(x => x.Id == document.Id)
                    .ConfigureAwait(false);
                createdInDatastore.Should()
                    .BeTrue();
            });
        }
    }
}
