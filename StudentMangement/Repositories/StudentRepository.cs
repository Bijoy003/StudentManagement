using Microsoft.EntityFrameworkCore;
using StudentMangement.Abstraction.Repositories;
using StudentMangement.Data;
using StudentMangement.Models;

namespace StudentMangement.Repositories
{
    public class StudentRepository : IStudentRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public StudentRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IEnumerable<Student>> GetAllAsync()
        {
            return await _dbContext.Student.ToListAsync();
        }

        public async Task<Student> GetByIdAsync(int id)
        {
            return await _dbContext.Student.FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task AddAsync(Student student)
        {
            await _dbContext.Student.AddAsync(student);
        }

        public void Update(Student student)
        {
            _dbContext.Student.Update(student);
        }

        public async Task DeleteAsync(int id)
        {
            var student = await GetByIdAsync(id);
            if (student != null)
                _dbContext.Student.Remove(student);
        }

        public async Task SaveAsync()
        {
            await _dbContext.SaveChangesAsync();
        }
    }
}
