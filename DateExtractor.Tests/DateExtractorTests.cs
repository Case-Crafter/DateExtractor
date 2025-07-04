using System;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using DateExtractor;
using Xunit;
using System.Text.RegularExpressions;

namespace DateExtractor.Tests
{
    public class DateExtractorTests
    {
        // ---------- TEXT-CLEANER TESTS ---------- //

        [Fact]
        public void Cleanup_RemovesControlChars()
        {
            // Arrange
            var cleaner = new TextCleaner();
            var input = "abc\u0000def";

            // Act
            var cleaned = cleaner.Clean(input);

            // Assert
            cleaned.Should().Be("abcdef");
            cleaned.Should().NotContain($"{'\u0000'}");
        }

        [Fact]
        public void Cleanup_JoinsMultilineDates()
        {
            // Arrange
            var cleaner = new TextCleaner();
            var input = "12.\n05.2025";

            // Act
            var cleaned = cleaner.Clean(input);

            // Assert
            cleaned.Should().Be("12. 05.2025");
        }

        [Fact]
        public void Cleanup_ConvertsToLowercase()
        {
            // Arrange
            var cleaner = new TextCleaner();
            var input = "JANUARY 01, 2025";

            // Act
            var cleaned = cleaner.Clean(input);

            // Assert
            cleaned.Should().Be("january 01, 2025");
        }


        [Fact]
        public void Cleanup_ReplacesHyphenDelimiter_LastCharRule()
        {
            // A hyphen in the delimiter list could break the regex build.
            var cleaner = new TextCleaner("/-");
            cleaner.Clean("07-04-2025").Should().Be("07 04 2025");
        }

        [Fact]
        public void Cleanup_DoesNotTurnDecimalsIntoDates()
        {
            var cleaner = new TextCleaner("/.-,");     // normal replacement
            var cleaned = cleaner.Clean("pi = 3.1415");
            cleaned.Should().Be("pi = 3 1415");        // dot → space
            // Feeds through extractor to guarantee no false-positive match
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            ex.Extract(cleaned).Should().BeEmpty();
        }

        // ---------- DATE-EXTRACTOR TESTS ---------- //

        [Fact]
        public void Extract_SimpleUSDate()
        {
            // Arrange
            var culture = new CultureInfo("en-US");
            var extractor = new DateExtractor([culture]);
            var input = "Invoice 04/07/2025";
            

            // Act
            var dates = extractor.Extract(input).ToList();

            // Assert
            dates.Should().ContainSingle()
                  .Which.Should().Be(new DateTime(2025, 4, 7));
        }

        [Fact]
        public void Extract_LongUSDate()
        {
            // Arrange
            
            var input = "Invoice date is November 1, 2019";
            var culture = new CultureInfo("en-US");
            var extractor = new DateExtractor([culture]);

            // Act
            var dates = extractor.Extract(input).ToList();

            // Assert
            dates.Should().ContainSingle()
                .Which.Should().Be(new DateTime(2019, 11, 1));
        }

        [Fact]
        public void Extract_IgnoresFalsePositives()
        {
            // Arrange
            var input = "12/34/5678";   // impossible date
            var culture = new CultureInfo("en-US");
            var extractor = new DateExtractor([culture]);

            // Act
            var dates = extractor.Extract(input);

            // Assert
            dates.Should().BeEmpty();
        }

        [Fact]
        public void Extract_MultipleCultures()
        {
            // Arrange
           
            var dateString = "04.07.2025, 4 Januar, 2025"; // ambiguous:   nb-NO => 4 July 2025; en-US would ignore dot format
            var nbCulture = new CultureInfo("nb-NO");
            var usCulture = new CultureInfo("en-US");
            var extractor = new DateExtractor([usCulture, nbCulture]);

            // Act
            var dates = extractor.Extract(dateString).ToList();

            // Assert
            dates.Should().HaveCount(2); //.Which.Should().Be(new DateTime(2025, 7, 4)); // 4 July
                                                              // not a valid US-pattern
            dates[0].Should().BeSameDateAs(new DateTime(2025, 4, 7));
            dates[1].Should().BeSameDateAs(new DateTime(2025, 1, 4));
        }

        [Fact]
        public void Extract_Duplicate()
        {
            // Arrange
            var culture = new CultureInfo("en-US");
            var extractor = new DateExtractor([culture]);
            var input = "Invoice dated 03-02-2024 (03-02-2024)";
            //var culture = new CultureInfo("en-GB");   // d-M-yyyy

            // Act
            var dates = extractor.Extract(input).ToList();

            // Assert
            dates.Should().HaveCount(2);
            dates.Should().AllBeEquivalentTo(new DateTime(2024, 3, 2));
        }

        [Fact]
        public void Invalid_Culture()
        {
            // Arrange – pick a culture code you haven’t provided a JSON for
            var cultures = new List<CultureInfo> { new CultureInfo("zz-ZZ") };

            // Act
            Action act = () => new DateExtractor(cultures);

            // Assert
            act.Should()
                .Throw<NotSupportedException>();
            //.WithMessage("*zz-ZZ*");   // optional: match message text
        }


        [Fact]
        public void Extract_ISODate_RecognisedOnceAcrossCultures()
        {
            var input = "Timestamp 2025-07-04 13:00:00";
            var cultures = new[] { new CultureInfo("nb-NO"), new CultureInfo("en-US") };
            var ex = new DateExtractor(cultures.ToList());

            var dates = ex.Extract(input).ToList();

            dates.Should().ContainSingle()
                 .Which.Should().Be(new DateTime(2025, 7, 4));
        }

        [Fact]
        public void Extract_ShortNorwegianMonth_TwoDigitYear()
        {
            var input = "Møtet er satt til 4 jul 25.";
            var ex = new DateExtractor([new CultureInfo("nb-NO")]);

            var dt = ex.Extract(input).Single();
            dt.Should().Be(new DateTime(2025, 7, 4));
        }

        [Fact]
        public void Extract_TwoDigitYear_EnUsCenturyRule()
        {
            // yy between 00-29 → 2000-2029  (uses system calendar pivot)
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var dt = ex.Extract("budget 7 4 25").Single();
            dt.Year.Should().Be(2025);
        }

        [Fact]
        public void Extract_LeapYearValidation()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var ok = ex.Extract("Due 02/29/2024").Single();   // leap year
            ok.Should().Be(new DateTime(2024, 2, 29));

            ex.Extract("02/29/2023").Should().BeEmpty();        // invalid date
        }

        [Fact]
        public void Extract_MultiLineIso_DuplicateAcrossLines()
        {
            /* Same ISO date appears on two separate pages; duplicates preserved. */
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var input = "page1: 2025-07-04\n\n--- page break ---\n\n2025-07-04 footer";
            ex.Extract(input).Should().HaveCount(2);
        }

        [Fact]
        public void Extract_CustomJsonWithRawDelimiters()
        {
            const string json = """
            {
              "culture":"fr-FR",
              "delimiters":"",                 // raw-delimiter mode
              "patterns":[
                {
                  "regex":"\\b\\d{2}/\\d{2}/\\d{4}\\b",
                  "format":"dd/MM/yyyy"
                }
              ]
            }
            """;
            var ex = new DateExtractor(new List<string> { json });
            var date = ex.Extract("Contrat signé le 04/07/2025").Single();
            date.Should().Be(new DateTime(2025, 7, 4));
        }

        [Fact]
        public void Extract_OverlapIsIgnoredAcrossCultures()
        {
            // "04 07 2025" is valid for both en-US & nb-NO after cleaning.
            // en-US is first -> should suppress nb-NO overlap.
            var input = "04.07.2025";
            var ex = new DateExtractor([ new CultureInfo("en-US"),
                                            new CultureInfo("nb-NO") ]);

            var dates = ex.Extract(input).ToList();
            dates.Should().HaveCount(1);
            dates[0].Should().Be(new DateTime(2025, 4, 7));   // en-US parse only
        }



        [Fact]
        public void Extract_MonthAbbrevWithPeriod()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var dt = ex.Extract("Report dated Nov. 15, 2019").Single();
            dt.Should().Be(new DateTime(2019, 11, 15));
        }

        [Fact]
        public void Extract_DateWithTimeSuffix()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var dt = ex.Extract("03/27/17 2:41").Single();
            dt.Should().Be(new DateTime(2017, 3, 27));
        }

        [Fact]
        public void Extract_DayFirstEnglish()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var dt = ex.Extract("19 Jul 1942").Single();
            dt.Should().Be(new DateTime(1942, 7, 19));
        }

        [Fact]
        public void Extract_DayFirstLongEnglish()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var dt = ex.Extract("19 August 1942").Single();
            dt.Should().Be(new DateTime(1942, 8, 19));
        }

        [Fact]
        public void Extract_OrdinalWithoutYearNotMatched()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            ex.Extract("Event on August 22nd").Should().BeEmpty();
        }

        [Fact]
        public void Extract_FirstOrdinalMatched()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var dt = ex.Extract("Event on August 1st, 2022").Single();
            dt.Should().Be(new DateTime(2022, 8, 1));
        }

        [Fact]
        public void Extract_SecondOrdinalMatched()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var dt = ex.Extract("Event on August 2nd, 2022").Single();
            dt.Should().Be(new DateTime(2022, 8, 2));
        }

        [Fact]
        public void Extract_ThirdOrdinalMatched()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var dt = ex.Extract("Event on August 3rd, 2022").Single();
            dt.Should().Be(new DateTime(2022, 8, 3));
        }

        [Fact]
        public void Extract_OrdinalMatched()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            var dt = ex.Extract("Event on August 22th, 2022").Single();
            dt.Should().Be(new DateTime(2022, 8, 22));
        }

        [Fact]
        public void Extract_ThreeDigitYearRejected()
        {
            var ex = new DateExtractor([new CultureInfo("en-US")]);
            ex.Extract("14 Jul 123").Should().BeEmpty();
        }

        [Fact]
        public void Extract_SpanishMasculineOrdinal()
        {
            const string esJson = """
                                  {
                                    "culture":"es-ES",
                                    "delimiters":"/.-,",
                                    "ordinals":["º","ª"],
                                    "patterns":[
                                      { "regex":"\\b\\d{1,2}\\s+de\\s+\\w+\\s+\\d{4}\\b",
                                        "format":"d 'de' MMMM yyyy" }
                                    ]
                                  }
                                  """;

            var ex = new DateExtractor(new List<string> { esJson });
            var dt = ex.Extract("Firmado el 1º de enero 2025").Single();
            dt.Should().Be(new DateTime(2025, 1, 1));
        }


        [Fact]
        public void Extract_Spanish_LongMonth()
        {
            // Arrange
            var cultures = new List<CultureInfo> { new CultureInfo("es-ES") };
            var extractor = new DateExtractor(cultures);
            var input = "El contrato es del 17 de abril 2025";

            // Act
            var dt = extractor.Extract(input).Single();

            // Assert
            dt.Should().Be(new DateTime(2025, 4, 17));
        }

        [Fact]
        public void Extract_Spanish_ShortMonth()
        {
            // Arrange
            var cultures = new List<CultureInfo> { new CultureInfo("es-ES") };
            var extractor = new DateExtractor(cultures);
            var input = "El contrato es del 17 ene 2025";

            // Act
            var dt = extractor.Extract(input).Single();

            // Assert
            dt.Should().Be(new DateTime(2025, 1, 17));
        }


        [Fact]
        public void Extract_Spanish_OrdinalFirst()
        {
            var cultures = new List<CultureInfo> { new CultureInfo("es-ES") };
            var extractor = new DateExtractor(cultures);
            var input = "Firmado el 1º de enero 2025";

            var dt = extractor.Extract(input).Single();
            dt.Should().Be(new DateTime(2025, 1, 1));
        }

        [Fact]
        public void Extract_Spanish_Numeric()
        {
            var extractor = new DateExtractor(
                new List<CultureInfo> { new CultureInfo("es-ES") });
            var input = "La fecha límite es 17/4/25";

            var dt = extractor.Extract(input).Single();
            dt.Should().Be(new DateTime(2025, 4, 17));
        }

        /*───────────────────────────  PORTUGUESE  ──────────────────────────*/

        [Fact]
        public void Extract_Portuguese_OrdinalFirst()
        {
            var extractor = new DateExtractor(
                new List<CultureInfo> { new CultureInfo("pt-BR") });
            var input = "Acordo celebrado em 1.º maio 2025";

            var dt = extractor.Extract(input).Single();
            dt.Should().Be(new DateTime(2025, 5, 1));
        }

        [Fact]
        public void Extract_Portuguese_ShortMonth()
        {
            var extractor = new DateExtractor(
                new List<CultureInfo> { new CultureInfo("pt-BR") });
            var input = "Relatório gerado em 17 abr 2025";

            var dt = extractor.Extract(input).Single();
            dt.Should().Be(new DateTime(2025, 4, 17));
        }

        /*──────────────────────────  MIXED & EDGE  ────────────────────────*/

        [Fact]
        public void Extract_MixedCultures_PriorityOrder()
        {
            // Same numeric date looks like M/D/Y for en-US and D/M/Y for es-ES.
            // With en-US first, overlap rule should keep only the US result.
            var cultures = new List<CultureInfo>
            {
                new CultureInfo("en-US"),
                new CultureInfo("es-ES")
            };
            var extractor = new DateExtractor(cultures);
            var input = "04/07/2025";

            var dates = extractor.Extract(input).ToList();

            dates.Should().ContainSingle()
                 .Which.Should().Be(new DateTime(2025, 4, 7)); // US parse
        }

        [Fact]
        public void Extract_OrdinalWithoutYear_IsIgnored()
        {
            var extractor = new DateExtractor(
                new List<CultureInfo> { new CultureInfo("es-ES") });
            var input = "Fiesta el 22º de agosto";

            extractor.Extract(input).Should().BeEmpty();
        }

    }
}

