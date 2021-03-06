﻿using System;
using System.IO;
using mRemoteNG.Config.DataProviders;
using mRemoteNGTests.TestHelpers;
using NUnit.Framework;

namespace mRemoteNGTests.Config.DataProviders
{
    public class FileDataProviderTests
    {
        private FileDataProvider _dataProvider;
        private string _testFilePath;

        [SetUp]
        public void Setup()
        {
            _testFilePath = FileTestHelpers.NewTempFilePath();
            FileTestHelpers.DeleteTestFile(_testFilePath);
            _dataProvider = new FileDataProvider(_testFilePath);
        }

        [TearDown]
        public void Teardown()
        {
            FileTestHelpers.DeleteTestFile(_testFilePath);
        }

        [Test]
        public void SetsFileContent()
        {
            Assert.That(File.Exists(_testFilePath), Is.False);
            var expectedFileContent = Guid.NewGuid().ToString();
            _dataProvider.Save(expectedFileContent);
            var fileContent = File.ReadAllText(_testFilePath);
            Assert.That(fileContent, Is.EqualTo(expectedFileContent));
        }

        [Test]
        public void LoadingFileThatDoesntExistProvidesEmptyString()
        {
            var fileThatShouldntExist = Guid.NewGuid().ToString();
            var dataProvider = new FileDataProvider(fileThatShouldntExist);
            var loadedData = dataProvider.Load();
            Assert.That(loadedData, Is.Empty);
        }
    }
}