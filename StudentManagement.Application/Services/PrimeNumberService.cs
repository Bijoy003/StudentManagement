using StudentManagement.Application.Interfaces;

namespace StudentManagement.Application.Services
{
    public class PrimeNumberService : IPrimeNumberService
    {
        public int GetNthPrime(int n)
        {
            if (n <= 0)
                throw new ArgumentException("n must be greater than zero.");

            int count = 0;
            int number = 1;

            while (count < n)
            {
                number++;
                if (IsPrime(number))
                    count++;
            }

            return number;
        }

        public bool IsPrime(int number)
        {
            if (number < 2) return false;
            for (int i = 2; i * i <= number; i++)
            {
                if (number % i == 0)
                    return false;
            }
            return true;
        }
    }
}
