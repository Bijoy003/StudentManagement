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

        public IEnumerable<Student> GetAll()
        {
            return _dbContext.Student.ToList();
        }

        public Student GetById(int id)
        {
            return _dbContext.Student.FirstOrDefault(s => s.Id == id);
        }

        public void Add(Student student)
        {
            _dbContext.Student.Add(student);
        }

        public void Update(Student student)
        {
            _dbContext.Student.Update(student);
        }

        public void Delete(int id)
        {
            var student = GetById(id);
            if (student != null)
                _dbContext.Student.Remove(student);
        }

        public void Save()
        {
            _dbContext.SaveChanges();
        }
    }
}
