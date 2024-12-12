namespace ERP_Billing
{
    using Microsoft.Extensions.Configuration;

    public class Program
    {
        public static void Main()
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false);

            IConfiguration config = builder.Build();
            ERPMonthlyBill eRPMonthlyBill = new(config);
            eRPMonthlyBill.LoadDataAndGeneratoMonthlyBill();
        }
    }
}