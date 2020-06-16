using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
#if BASELINE
using MySql.Data.MySqlClient;
#else
using MySqlConnector;
#endif
using Xunit;
using Xunit.Sdk;

namespace SideBySide
{
	[Collection("BulkLoaderCollection")]
	public class BulkLoaderAsync : IClassFixture<DatabaseFixture>
	{
		public BulkLoaderAsync(DatabaseFixture database)
		{
			m_testTable = "BulkLoaderAsyncTest";
			var initializeTable = $@"
				drop table if exists {m_testTable};
				create table {m_testTable}
				(
					one int primary key
					, ignore_one int
					, two varchar(200)
					, ignore_two varchar(200)
					, three varchar(200)
					, four datetime
					, five blob
				) CHARACTER SET = UTF8;";
			database.Connection.Execute(initializeTable);

			m_memoryStreamBytes = System.Text.Encoding.UTF8.GetBytes(@"1,'two-1','three-1'
2,'two-2','three-2'
3,'two-3','three-3'
4,'two-4','three-4'
5,'two-5','three-5'
");
		}

		[SkippableFact(ConfigSettings.TsvFile)]
		public async Task BulkLoadTsvFile()
		{
			using var connection = new MySqlConnection(GetConnectionString());
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			bl.FileName = AppConfig.MySqlBulkLoaderTsvFile;
			bl.TableName = m_testTable;
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = false;
			int rowCount = await bl.LoadAsync();
			Assert.Equal(20, rowCount);
		}

		[SkippableFact(ConfigSettings.LocalTsvFile)]
		public async Task BulkLoadLocalTsvFile()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			bl.FileName = AppConfig.MySqlBulkLoaderLocalTsvFile;
			bl.TableName = m_testTable;
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = true;
			int rowCount = await bl.LoadAsync();
			Assert.Equal(20, rowCount);
		}

		[SkippableFact(ConfigSettings.CsvFile)]
		public async Task BulkLoadCsvFile()
		{
			using var connection = new MySqlConnection(GetConnectionString());
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			bl.FileName = AppConfig.MySqlBulkLoaderCsvFile;
			bl.TableName = m_testTable;
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = false;
			int rowCount = await bl.LoadAsync();
			Assert.Equal(20, rowCount);
		}

		[SkippableFact(ConfigSettings.LocalCsvFile)]
		public async Task BulkLoadLocalCsvFile()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			bl.FileName = AppConfig.MySqlBulkLoaderLocalCsvFile;
			bl.TableName = m_testTable;
			bl.CharacterSet = "UTF8";
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = true;
			int rowCount = await bl.LoadAsync();
			Assert.Equal(20, rowCount);
		}

		[Fact]
		public async Task BulkLoadCsvFileNotFound()
		{
			using var connection = new MySqlConnection(GetConnectionString());
			await connection.OpenAsync();

			var secureFilePath = await connection.ExecuteScalarAsync<string>(@"select @@global.secure_file_priv;");
			if (string.IsNullOrEmpty(secureFilePath) || secureFilePath == "NULL")
				return;

			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			bl.FileName = Path.Combine(secureFilePath, AppConfig.MySqlBulkLoaderCsvFile + "-junk");
			bl.TableName = m_testTable;
			bl.CharacterSet = "UTF8";
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = false;
			try
			{
				int rowCount = await bl.LoadAsync();
			}
			catch (Exception exception)
			{
				while (exception.InnerException is not null)
					exception = exception.InnerException;

				if (exception is not FileNotFoundException)
				{
					try
					{
						Assert.Contains("Errcode: 2 ", exception.Message, StringComparison.OrdinalIgnoreCase);
					}
					catch (ContainsException)
					{
						Assert.Contains("OS errno 2 ", exception.Message, StringComparison.OrdinalIgnoreCase);
					}
					Assert.Contains("No such file or directory", exception.Message);
				}
			}
		}

		[Fact]
		public async Task BulkLoadLocalCsvFileNotFound()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			bl.Timeout = 3; //Set a short timeout for this test because the file not found exception takes a long time otherwise, the timeout does not change the result
			bl.FileName = AppConfig.MySqlBulkLoaderLocalCsvFile + "-junk";
			bl.TableName = m_testTable;
			bl.CharacterSet = "UTF8";
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = true;
			try
			{
				int rowCount = await bl.LoadAsync();
			}
			catch (MySqlException mySqlException)
			{
				while (mySqlException.InnerException is not null)
				{
					if (mySqlException.InnerException is MySqlException innerException)
					{
						mySqlException = innerException;
					}
					else
					{
						Assert.IsType<System.IO.FileNotFoundException>(mySqlException.InnerException);
						break;
					}
				}
				if (mySqlException.InnerException is null)
				{
					Assert.IsType<System.IO.FileNotFoundException>(mySqlException);
				}
			}
			catch (Exception exception)
			{
				//We know that the exception is not a MySqlException, just use the assertion to fail the test
				Assert.IsType<MySqlException>(exception);
			}
		}

		[SkippableFact(ConfigSettings.LocalCsvFile)]
		public async Task BulkLoadLocalCsvFileInTransactionWithCommit()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			using (var transaction = connection.BeginTransaction())
			{
				var bulkLoader = new MySqlBulkLoader(connection)
				{
					FileName = AppConfig.MySqlBulkLoaderLocalCsvFile,
					TableName = m_testTable,
					CharacterSet = "UTF8",
					NumberOfLinesToSkip = 1,
					FieldTerminator = ",",
					FieldQuotationCharacter = '"',
					FieldQuotationOptional = true,
					Local = true,
				};
				bulkLoader.Expressions.Add("five = UNHEX(five)");
				bulkLoader.Columns.AddRange(new[] { "one", "two", "three", "four", "five" });

				var rowCount = await bulkLoader.LoadAsync();
				Assert.Equal(20, rowCount);

				transaction.Commit();
			}

			Assert.Equal(20, await connection.ExecuteScalarAsync<int>($@"select count(*) from {m_testTable};"));
		}

		[SkippableFact(ConfigSettings.LocalCsvFile)]
		public async Task BulkLoadLocalCsvFileInTransactionWithRollback()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			using (var transaction = connection.BeginTransaction())
			{
				var bulkLoader = new MySqlBulkLoader(connection)
				{
					FileName = AppConfig.MySqlBulkLoaderLocalCsvFile,
					TableName = m_testTable,
					CharacterSet = "UTF8",
					NumberOfLinesToSkip = 1,
					FieldTerminator = ",",
					FieldQuotationCharacter = '"',
					FieldQuotationOptional = true,
					Local = true,
				};
				bulkLoader.Expressions.Add("five = UNHEX(five)");
				bulkLoader.Columns.AddRange(new[] { "one", "two", "three", "four", "five" });

				var rowCount = await bulkLoader.LoadAsync();
				Assert.Equal(20, rowCount);

				transaction.Rollback();
			}

			Assert.Equal(0, await connection.ExecuteScalarAsync<int>($@"select count(*) from {m_testTable};"));
		}

		[Fact]
		public async Task BulkLoadMissingFileName()
		{
			using var connection = new MySqlConnection(GetConnectionString());
			await connection.OpenAsync();
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			bl.TableName = m_testTable;
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = false;
#if BASELINE
			await Assert.ThrowsAsync<System.NullReferenceException>(async () =>
			{
				int rowCount = await bl.LoadAsync();
			});
#else
			await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
			{
				int rowCount = await bl.LoadAsync();
			});
#endif
		}

		[SkippableFact(ConfigSettings.LocalCsvFile)]
		public async Task BulkLoadMissingTableName()
		{
			using var connection = new MySqlConnection(GetConnectionString());
			await connection.OpenAsync();
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			bl.FileName = AppConfig.MySqlBulkLoaderLocalCsvFile;
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = false;
#if BASELINE
			await Assert.ThrowsAsync<MySqlException>(async () =>
			{
				int rowCount = await bl.LoadAsync();
			});
#else
			await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
			{
				int rowCount = await bl.LoadAsync();
			});
#endif
		}

#if !BASELINE
		[SkippableFact(ConfigSettings.LocalCsvFile)]
		public async Task BulkLoadFileStreamInvalidOperation()
		{
			using var connection = new MySqlConnection(GetConnectionString());
			var bl = new MySqlBulkLoader(connection);
			using var fileStream = new FileStream(AppConfig.MySqlBulkLoaderLocalCsvFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
			bl.SourceStream = fileStream;
			bl.TableName = m_testTable;
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = false;
			await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
			{
				int rowCount = await bl.LoadAsync();
			});
		}

		[SkippableFact(ConfigSettings.LocalCsvFile)]
		public async Task BulkLoadLocalFileStream()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			using var fileStream = new FileStream(AppConfig.MySqlBulkLoaderLocalCsvFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
			bl.SourceStream = fileStream;
			bl.TableName = m_testTable;
			bl.Columns.AddRange(new string[] { "one", "two", "three", "four", "five" });
			bl.NumberOfLinesToSkip = 1;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Expressions.Add("five = UNHEX(five)");
			bl.Local = true;
			int rowCount = await bl.LoadAsync();
			Assert.Equal(20, rowCount);
		}

		[Fact]
		public async Task BulkLoadMemoryStreamInvalidOperation()
		{
			using var connection = new MySqlConnection(GetConnectionString());
			await connection.OpenAsync();
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			using var memoryStream = new MemoryStream(m_memoryStreamBytes, false);
			bl.SourceStream = memoryStream;
			bl.TableName = m_testTable;
			bl.Columns.AddRange(new string[] { "one", "two", "three" });
			bl.NumberOfLinesToSkip = 0;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Local = false;
			await Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
			{
				int rowCount = await bl.LoadAsync();
			});
		}

		[Fact]
		public async Task BulkLoadLocalMemoryStream()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			MySqlBulkLoader bl = new MySqlBulkLoader(connection);
			using var memoryStream = new MemoryStream(m_memoryStreamBytes, false);
			bl.SourceStream = memoryStream;
			bl.TableName = m_testTable;
			bl.Columns.AddRange(new string[] { "one", "two", "three" });
			bl.NumberOfLinesToSkip = 0;
			bl.FieldTerminator = ",";
			bl.FieldQuotationCharacter = '"';
			bl.FieldQuotationOptional = true;
			bl.Local = true;
			int rowCount = await bl.LoadAsync();
			Assert.Equal(5, rowCount);
		}

		[Fact]
		public async Task BulkCopyDataReader()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			using var connection2 = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			await connection2.OpenAsync();
			using (var cmd = new MySqlCommand(@"drop table if exists bulk_load_data_reader_source;
drop table if exists bulk_load_data_reader_destination;
create table bulk_load_data_reader_source(value int, name text);
create table bulk_load_data_reader_destination(value int, name text);
insert into bulk_load_data_reader_source values(0, 'zero'),(1,'one'),(2,'two'),(3,'three'),(4,'four'),(5,'five'),(6,'six');", connection))
			{
				await cmd.ExecuteNonQueryAsync();
			}

			using (var cmd = new MySqlCommand("select * from bulk_load_data_reader_source;", connection))
			using (var reader = await cmd.ExecuteReaderAsync())
			{
				var bulkCopy = new MySqlBulkCopy(connection2) { DestinationTableName = "bulk_load_data_reader_destination", };
				await bulkCopy.WriteToServerAsync(reader);
			}

			using var cmd1 = new MySqlCommand("select * from bulk_load_data_reader_source order by value;", connection);
			using var cmd2 = new MySqlCommand("select * from bulk_load_data_reader_destination order by value;", connection2);
			using var reader1 = await cmd1.ExecuteReaderAsync();
			using var reader2 = await cmd2.ExecuteReaderAsync();
			while (await reader1.ReadAsync())
			{
				Assert.True(await reader2.ReadAsync());
				Assert.Equal(reader1.GetInt32(0), reader2.GetInt32(0));
				Assert.Equal(reader1.GetString(1), reader2.GetString(1));
			}
			Assert.False(await reader2.ReadAsync());
		}

#if !NETCOREAPP1_1_2
		[Fact]
		public void BulkCopyNullDataTable()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			connection.Open();
			var bulkCopy = new MySqlBulkCopy(connection);
			Assert.ThrowsAsync<ArgumentNullException>(async () => await bulkCopy.WriteToServerAsync(default(DataTable)));
		}

		[Fact]
		public async Task BulkCopyDataTableWithLongData()
		{
			var dataTable = new DataTable()
			{
				Columns =
				{
					new DataColumn("id", typeof(int)),
					new DataColumn("data", typeof(byte[])),
				},
				Rows =
				{
					new object[] { 1, new byte[524200] },
					new object[] { 12345678, new byte[524200] },
				},
			};

			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			using (var cmd = new MySqlCommand(@"drop table if exists bulk_load_data_table;
create table bulk_load_data_table(a int, b longblob);", connection))
			{
				await cmd.ExecuteNonQueryAsync();
			}

			var bulkCopy = new MySqlBulkCopy(connection)
			{
				DestinationTableName = "bulk_load_data_table",
			};
			await bulkCopy.WriteToServerAsync(dataTable);
		}

		[Fact]
		public async Task BulkCopyDataTableWithTooLongData()
		{
			var dataTable = new DataTable()
			{
				Columns =
				{
					new DataColumn("data", typeof(byte[])),
				},
				Rows =
				{
					new object[] { new byte[524300] },
				}
			};

			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			using (var cmd = new MySqlCommand(@"drop table if exists bulk_load_data_table;
create table bulk_load_data_table(a int, b longblob);", connection))
			{
				await cmd.ExecuteNonQueryAsync();
			}

			var bulkCopy = new MySqlBulkCopy(connection)
			{
				DestinationTableName = "bulk_load_data_table",
				ColumnMappings =
				{
					new MySqlBulkCopyColumnMapping(0, "b"),
				}
			};
			try
			{
				await bulkCopy.WriteToServerAsync(dataTable);
				Assert.True(false, "Expected exception wasn't thrown");
			}
			catch (MySqlException ex) when (ex.InnerException?.InnerException is NotSupportedException)
			{
			}
		}

		[Theory]
		[InlineData(0, 15, 0, 0)]
		[InlineData(5, 15, 3, 15)]
		[InlineData(5, 16, 3, 15)]
		[InlineData(int.MaxValue, 0, 0, 0)]
		public async Task BulkCopyNotifyAfter(int notifyAfter, int rowCount, int expectedEventCount, int expectedRowsCopied)
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			using (var cmd = new MySqlCommand(@"drop table if exists bulk_copy_notify_after;
				create table bulk_copy_notify_after(value int);", connection))
			{
				await cmd.ExecuteNonQueryAsync();
			}

			var bulkCopy = new MySqlBulkCopy(connection)
			{
				NotifyAfter = notifyAfter,
				DestinationTableName = "bulk_copy_notify_after",
			};
			int eventCount = 0;
			long rowsCopied = 0;
			bulkCopy.MySqlRowsCopied += (s, e) =>
			{
				eventCount++;
				rowsCopied = e.RowsCopied;
				Assert.Equal(bulkCopy.RowsCopied, e.RowsCopied);
			};

			var dataTable = new DataTable()
			{
				Columns = { new DataColumn("value", typeof(int)) },
			};
			foreach (var x in Enumerable.Range(1, rowCount))
				dataTable.Rows.Add(new object[] { x });

			await bulkCopy.WriteToServerAsync(dataTable);
			Assert.Equal(expectedEventCount, eventCount);
			Assert.Equal(expectedRowsCopied, rowsCopied);
			Assert.Equal(rowCount, bulkCopy.RowsCopied);
		}

		[Theory]
		[InlineData(0, 40, 0, 0, 0, 40)]
		[InlineData(5, 40, 15, 3, 15, 0)]
		[InlineData(5, 40, 20, 4, 20, 16)]
		[InlineData(int.MaxValue, 20, 0, 0, 0, 20)]
		public async Task BulkCopyAbort(int notifyAfter, int rowCount, int abortAfter, int expectedEventCount, int expectedRowsCopied, long expectedCount)
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			await connection.OpenAsync();
			using (var cmd = new MySqlCommand(@"drop table if exists bulk_copy_abort;
				create table bulk_copy_abort(value longtext);", connection))
			{
				await cmd.ExecuteNonQueryAsync();
			}

			var bulkCopy = new MySqlBulkCopy(connection)
			{
				NotifyAfter = notifyAfter,
				DestinationTableName = "bulk_copy_abort",
			};
			int eventCount = 0;
			long rowsCopied = 0;
			bulkCopy.MySqlRowsCopied += (s, e) =>
			{
				eventCount++;
				rowsCopied = e.RowsCopied;
				if (e.RowsCopied >= abortAfter)
					e.Abort = true;
			};

			var dataTable = new DataTable()
			{
				Columns = { new DataColumn("value", typeof(string)) },
			};
			var str = new string('a', 62500);
			foreach (var x in Enumerable.Range(1, rowCount))
				dataTable.Rows.Add(new object[] { str });

			await bulkCopy.WriteToServerAsync(dataTable);
			Assert.Equal(expectedEventCount, eventCount);
			Assert.Equal(expectedRowsCopied, rowsCopied);

			using (var cmd = new MySqlCommand("select count(value) from bulk_copy_abort;", connection))
				Assert.Equal(expectedCount, await cmd.ExecuteScalarAsync());
		}
#endif

		[Fact]
		public void BulkCopyNullDataReader()
		{
			using var connection = new MySqlConnection(GetLocalConnectionString());
			connection.Open();
			var bulkCopy = new MySqlBulkCopy(connection);
			Assert.ThrowsAsync<ArgumentNullException>(async () => await bulkCopy.WriteToServerAsync(default(DbDataReader)));
		}
#endif

		private static string GetConnectionString() => BulkLoaderSync.GetConnectionString();
		private static string GetLocalConnectionString() => BulkLoaderSync.GetLocalConnectionString();

		readonly string m_testTable;
		readonly byte[] m_memoryStreamBytes;
	}
}
