﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Xunit;
using System;
using System.Collections.Generic;
using Roslyn.Test.Utilities;
using System.Text;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Microsoft.DiaSymReader.Tools.UnitTests
{
    public class PdbConverterPortableToWindowsTests
    {
        [Fact]
        public void ValidateSrcSvrVariables()
        {
            PdbConverterPortableToWindows.ValidateSrcSvrVariable("A", "", "");
            PdbConverterPortableToWindows.ValidateSrcSvrVariable("AZaz09_", "", "");
            PdbConverterPortableToWindows.ValidateSrcSvrVariable("ABC", "", "");

            Assert.Throws<ArgumentException>(() => PdbConverterPortableToWindows.ValidateSrcSvrVariable(null, "", ""));
            Assert.Throws<ArgumentException>(() => PdbConverterPortableToWindows.ValidateSrcSvrVariable("", "", ""));
            Assert.Throws<ArgumentException>(() => PdbConverterPortableToWindows.ValidateSrcSvrVariable("-", "", ""));
            Assert.Throws<ArgumentException>(() => PdbConverterPortableToWindows.ValidateSrcSvrVariable("ABC_[", "", ""));
            Assert.Throws<ArgumentException>(() => PdbConverterPortableToWindows.ValidateSrcSvrVariable("0ABC", "", ""));
            Assert.Throws<ArgumentException>(() => PdbConverterPortableToWindows.ValidateSrcSvrVariable("A", "a\r", ""));
            Assert.Throws<ArgumentException>(() => PdbConverterPortableToWindows.ValidateSrcSvrVariable("A", "a\n", ""));
            Assert.Throws<ArgumentException>(() => PdbConverterPortableToWindows.ValidateSrcSvrVariable("A", "a\0", ""));
        }

        private void ValidateSourceLinkConversion(string[] documents, string sourceLink, string expectedSrcSvr, PdbDiagnostic[] expectedErrors = null)
        {
            var actualErrors = new List<PdbDiagnostic>();
            var converter = new PdbConverterPortableToWindows(actualErrors.Add);
            var actualSrcSvr = converter.ConvertSourceServerData(sourceLink, documents, PortablePdbConversionOptions.Default);

            AssertEx.Equal(expectedErrors ?? Array.Empty<PdbDiagnostic>(), actualErrors);
            AssertEx.AssertLinesEqual(expectedSrcSvr, actualSrcSvr);
        }

        [Fact]
        public void SourceLinkConversion_NoDocs()
        {
            ValidateSourceLinkConversion(new string[0],
@"{
   ""documents"" : 
   {
      ""C:\\a*"": ""http://server/1/a*"",
   }
}",
            null);
        }

        [Fact]
        public void SourceLinkConversion_BOM()
        {
            ValidateSourceLinkConversion(new[]
            {
                @"C:\a\1.cs"
            },
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetString(Encoding.UTF8.GetBytes(
@"{
   ""documents"" : 
   {
      ""C:\\a*"": ""http://server/X/Y*"",
   }
}")),
@"
SRCSRV: ini ------------------------------------------------
VERSION=2
SRCSRV: variables ------------------------------------------
RAWURL=http://server/X/Y/1.cs%var2%
SRCSRVVERCTRL=http
SRCSRVTRG=%RAWURL%
SRCSRV: source files ---------------------------------------
C:\a\1.cs*
SRCSRV: end ------------------------------------------------
");
        }

        [Fact]
        public void SourceLinkConversion_EmptySourceLink()
        {
            ValidateSourceLinkConversion(new[]
            {
                @"C:\a\1.cs"
            },
@"{
   ""documents"" : 
   {
   }
}",
            null,
            new[]
            {
                new PdbDiagnostic(PdbDiagnosticId.UnmappedDocumentName, 0, new[] { @"C:\a\1.cs" }),
                new PdbDiagnostic(PdbDiagnosticId.NoSupportedUrlsFoundInSourceLink, 0, Array.Empty<object>())
            });
        }

        [Fact]
        public void SourceLinkConversion_BadJson_Key()
        {
            ValidateSourceLinkConversion(new[]
            {
                @"C:\a\1.cs"
            },            
@"{
   ""documents"" : 
   {
      1: ""http://server/X/Y*"",
   }
}",
            null, 
            new[]
            {
                new PdbDiagnostic(PdbDiagnosticId.InvalidSourceLink, 0, new[] { ConverterResources.InvalidJsonDataFormat }),
                new PdbDiagnostic(PdbDiagnosticId.UnmappedDocumentName, 0, new[] { @"C:\a\1.cs" }),
                new PdbDiagnostic(PdbDiagnosticId.NoSupportedUrlsFoundInSourceLink, 0, Array.Empty<object>())
            });
        }

        [Fact]
        public void SourceLinkConversion_BadJson_NullValue()
        {
            ValidateSourceLinkConversion(new[]
            {
                @"C:\a\1.cs",
                @"C:\a\2.cs",
            },
@"{
   ""documents"" : 
   {
      ""1"": null,
      ""2"": {},
      ""C:\\a*"": ""http://a/*"",
      ""*C:\\x*"": ""http://a/*"",
      ""C:\\x*"": ""*http://a/*"",
      ""C:\\x"": ""*http://a/"",
      ""C:\\x*"": ""http://a/""
   }
}",
@"
SRCSRV: ini ------------------------------------------------
VERSION=2
SRCSRV: variables ------------------------------------------
RAWURL=http://a//%var2%
SRCSRVVERCTRL=http
SRCSRVTRG=%RAWURL%
SRCSRV: source files ---------------------------------------
C:\a\1.cs*1.cs
C:\a\2.cs*2.cs
SRCSRV: end ------------------------------------------------
",
            new[]
            {
                new PdbDiagnostic(PdbDiagnosticId.InvalidSourceLink, 0, new[] { ConverterResources.InvalidJsonDataFormat })
            });
        }

        [Fact]
        public void SourceLinkConversion_BadJson2()
        {
            var json = @"{
   ""documents"" : 
   {
      1: ""http://server/X/Y*"",
};";

            Exception expectedException = null;
            try
            {
                JObject.Parse(json);
            }
            catch (JsonReaderException e)
            {
                expectedException = e;
            }

            ValidateSourceLinkConversion(new[]
            {
                @"C:\a\1.cs"
            },
            json,
            null,
            new[]
            {
                new PdbDiagnostic(PdbDiagnosticId.InvalidSourceLink, 0, new[] { expectedException.Message })
            });
        }

        [Fact]
        public void SourceLinkConversion_MalformedUrls()
        {
            ValidateSourceLinkConversion(new[]
            {
                @"C:\src\a\1.cs",
                @"C:\src\b\2.cs",
                @"C:\src\c\3.cs",
                @"D:",
            },
@"{
   ""documents"" : 
   {
      ""C:\\src\\a\\*"": ""http://server/1/%/*"",
      ""C:\\src\\b\\2.*"": ""*://server/2/"",
      ""C:\\src\\c\\*"": ""https://server/3/"",
      ""D*"": ""http*//server/2/a.cs"",
   }
}",
@"
SRCSRV: ini ------------------------------------------------
VERSION=2
SRCSRV: variables ------------------------------------------
RAWURL=https://server/3/3.cs%var2%
SRCSRVVERCTRL=https
SRCSRVTRG=%RAWURL%
SRCSRV: source files ---------------------------------------
C:\src\c\3.cs*
SRCSRV: end ------------------------------------------------",
            new[] 
            {
                new PdbDiagnostic(PdbDiagnosticId.MalformedSourceLinkUrl, 0, new[] { "http://server/1/%/1.cs" }),
                new PdbDiagnostic(PdbDiagnosticId.UrlSchemeIsNotHttp, 0, new[] { "cs://server/2/" }),
                new PdbDiagnostic(PdbDiagnosticId.MalformedSourceLinkUrl, 0, new[] { "http%3A//server/2/a.cs" }),
            });
        }
    }
}
