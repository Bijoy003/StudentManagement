using StudentMangement.Abstraction.Services;
using StudentMangement.Services;

namespace StudentManagement.Tests
{
    public class PrimeNumberServiceTests
    {
        private readonly IPrimeNumberService _primeNumberService;

        public PrimeNumberServiceTests()
        {
            _primeNumberService = new PrimeNumberService();
        }

        [Fact]
        public void GetNthPrime_FirstPrime_ShouldReturn2() =>
            Assert.Equal(2, _primeNumberService.GetNthPrime(1));

        [Fact]
        public void GetNthPrime_FifthPrime_ShouldReturn11() =>
            Assert.Equal(11, _primeNumberService.GetNthPrime(5));

        [Fact]
        public void GetNthPrime_Zero_ShouldThrowException() =>
            Assert.Throws<ArgumentException>(() => _primeNumberService.GetNthPrime(0));

        [Fact]
        public void GetNthPrime_TenthPrime_ShouldReturn29() =>
            Assert.Equal(29, _primeNumberService.GetNthPrime(10));

        [Fact]
        public void GetNthPrime_TenthPrime_ShouldReturn31() =>
            Assert.Equal(34, _primeNumberService.GetNthPrime(10));
    }
}
