namespace StudentMangement.Abstraction.Services
{
    public interface IPrimeNumberService
    {
        int GetNthPrime(int n);
        bool IsPrime(int number);
    }
}
