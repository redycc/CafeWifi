using CafeWiFi.Core;

namespace CafeWiFi.Tests;

public class PhonePasswordsTests
{
    [Fact]
    public void Candidates_TaipeiNumber_DropsAreaCodeFirstThenFullThenLeadingZero()
    {
        Assert.Equal(new[] { "23458613", "0223458613", "223458613" },
            PhonePasswords.Candidates("02-2345-8613"));
    }

    [Fact]
    public void Candidates_NonTaipeiNumber_DropsLeadingZeroFirstThenFullThenLast8()
    {
        Assert.Equal(new[] { "492334567", "0492334567", "92334567" },
            PhonePasswords.Candidates("049-233-4567"));
    }

    [Fact]
    public void Candidates_DropsExtension()
    {
        Assert.Equal(new[] { "12345678", "0212345678", "212345678" },
            PhonePasswords.Candidates("02-1234-5678 分機123"));
    }

    [Fact]
    public void Candidates_SkipsFormsOutsideLengthRange_AndDedupes()
    {
        // "23456789": leading-zero form is 7 digits (too short); full and last-8 are the same.
        Assert.Equal(new[] { "23456789" }, PhonePasswords.Candidates("23456789"));
    }

    [Fact]
    public void Candidates_EmptyOrNoDigits_ReturnsEmpty()
    {
        Assert.Empty(PhonePasswords.Candidates(""));
        Assert.Empty(PhonePasswords.Candidates(null));
        Assert.Empty(PhonePasswords.Candidates("no digits here"));
    }
}
