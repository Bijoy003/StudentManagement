namespace StudentManagement.Application.Interfaces
{
    public interface IPrimeNumberService
    {
        int GetNthPrime(int n);
        bool IsPrime(int number);
    }
}
