﻿using System;
using System.Reflection;
using System.Linq.Expressions;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace ConsoleApplication
{
    public class User {
        public long Id { get; set; }
        public int Age { get; set; }
        public string Name { get; set; }
        public double Weight { get; set; }
    }
    
    public class DB {
        private string _name;
        private SqliteConnection _con;
        private string _con_string;
        public DB(string db_name) {
            _name = db_name;
            _con_string = new SqliteConnectionStringBuilder{ DataSource = db_name }.ToString();
            _con = new SqliteConnection(_con_string);
            _con.Open();
        }
        
        public void Execute(string query, Object obj = null) {
            using (var transaction = _con.BeginTransaction()) {
                var cmd = _con.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = query;
                if (obj != null) {
                    var props = obj.GetType().GetProperties();
                    foreach (var prop in props) {
                        cmd.Parameters.AddWithValue($"${prop.Name}", prop.GetValue(obj));
                    }
                }
                cmd.ExecuteNonQuery();
                transaction.Commit();
            }
        }
        
        public IEnumerable<T> Query<T>(string query, Object parameters = null) where T : new()  {
            using (var transaction = _con.BeginTransaction()) {
                var cmd = _con.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = query;
                if (parameters != null) {
                    var props = parameters.GetType().GetProperties();
                    foreach (var prop in props) {
                        cmd.Parameters.AddWithValue($"${prop.Name}", prop.GetValue(parameters));
                    }
                }
                
                List<T> Ts = new List<T>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) {
                        var t = new T();
                        for( var inx = 0; inx < reader.FieldCount; ++inx) {
                            var col_name = reader.GetName(inx);
                            var col_type = reader.GetFieldType(inx).Name;
                            PropertyInfo prop = t.GetType().GetProperty(col_name, BindingFlags.Public | BindingFlags.Instance);
                            if(null != prop && prop.CanWrite) {
                                var val = Convert.ChangeType(reader.GetValue(inx), prop.PropertyType);
                                prop.SetValue(t, val, null);
                            }
                        }
                        Ts.Add(t);
                    }
                }

                transaction.Commit();
                return Ts;
            }
        }
        
        public QueryBuilder<T> Query<T>() where T : new() {
            var query = $"SELECT * from {typeof(T).Name} ";
            return new QueryBuilder<T>(this, query);
        }
          
        private Dictionary<string,string> _type_mappings = new Dictionary<string,string> {
            {"Int64","integer"},
            {"Int32","int"},
            {"String","text"},
            {"Double","real"},
        };
        
        public void CreateTableIfNotExists<T>() {
            var props = typeof(T).GetProperties().Select(prop => {
                var name  = prop.Name;
                var type = prop.PropertyType.Name;
                var mapped_type = _type_mappings[type];
                var primary_key = name == "Id" ? " primary key autoincrement" : "";
                return $"{name} {mapped_type}{primary_key}";
            });
            
            var rows = string.Join(", ", props);
            var query = $@"CREATE TABLE IF NOT EXISTS {typeof(T).Name} ({rows});";
            Execute(query);
            //Console.WriteLine(query);
        }
    }
    
    public class QueryBuilder<T>  where T: new() {
        private string _query;
        private DB _db;
        public QueryBuilder(DB db, string query) {
            _query = query;
            _db = db;
        }
        public QueryBuilder<T> Where(Expression<Func<T,Object>> expr) {
            string field = null;
            string method = null;
            string argument = null;
            string condition = null;
            if(expr.Body is UnaryExpression) {
                var body = expr.Body as UnaryExpression;
                if(body.Operand is BinaryExpression) {
                    var operand = body.Operand as BinaryExpression;
                    field = (operand.Left as MemberExpression).Member.Name;
                    method = operand.NodeType.ToString();
                    argument = (operand.Right as ConstantExpression).Value.ToString();
                }
                if (body.Operand is MethodCallExpression) {
                   var operand = body.Operand as MethodCallExpression; 
                   method = operand.Method.Name;
                   field = (operand.Object as MemberExpression).Member.Name;
                   argument = (operand.Arguments[0] as ConstantExpression).Value.ToString();
                }
               
                if(field == null) {
                    return this;
                }
                if(method == "GreaterThan") condition = $"{field} > {argument}";
                if(method == "LessThan") condition = $"{field} < {argument}";
                if(method == "StartsWith") condition = $"{field} LIKE '{argument}%'";
                if(method == "EndsWith") condition = $"{field} LIKE '%{argument}'";
                if(method == "Contains") condition = $"{field} LIKE '%{argument}%'";
                
                _query = $"{_query} WHERE {condition}";
            }
            return this;
        }
        
        public IEnumerable<T> Run() {
            return _db.Query<T>(_query);
        }
    }
    
    public class Program {
        public static void Main(string[] args) {
            var db = new DB(":memory:");
            db.CreateTableIfNotExists<User>();
            
            var query = "INSERT INTO User (Age,Name,Weight) values ($age,$name,$weight);";
            db.Execute(query, new { age = 21, name = "joey", weight = 80.0 });
            db.Execute(query, new { age = 22, name = "chandler", weight = 60.0 });
            db.Execute(query, new { age = 23, name = "monica", weight = 50.0 });
            db.Execute(query, new { age = 24, name = "ross", weight = 75.0 });
            db.Execute(query, new { age = 25, name = "phoebe", weight = 45.0 });
            db.Execute(query, new { age = 26, name = "rachel", weight = 50.0 });
            
            
            foreach (var user in db.Query<User>("Select * from User")) {
                Console.WriteLine($"{user.Id} {user.Name} {user.Age} {user.Weight}");
            }
            Console.WriteLine("-----------------------------------------------------");
            foreach(var user in db.Query<User>().Where(u => u.Age > 23).Run()) {
                Console.WriteLine($"{user.Id} {user.Name} {user.Age} {user.Weight}");
            }
            Console.WriteLine("-----------------------------------------------------");
            foreach(var user in db.Query<User>().Where(u => u.Name.StartsWith("r")).Run()) {
                Console.WriteLine($"{user.Id} {user.Name} {user.Age} {user.Weight}");
            }
            
        }
    }
}
