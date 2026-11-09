import psycopg

conn_str = "host=postgres port=5432 dbname=digism_pos user=postgres password=postgres"
with psycopg.connect(conn_str) as conn:
    with conn.cursor() as cur:
        tables = ["Users", "Categories", "Products", "Customers", "Suppliers", "Warehouses", "WarehouseStocks", "Shifts", "Orders", "OrderItems", "StockLedger", "PosLogs"]
        print("=== DATABASE SEED VERIFICATION ===")
        for t in tables:
            cur.execute(f'SELECT COUNT(*) FROM "{t}"')
            count = cur.fetchone()[0]
            print(f"Table {t}: {count} rows")

        cur.execute('SELECT SUM("Total") FROM "Orders"')
        total_revenue = cur.fetchone()[0]
        print(f"\nTotal Order Revenue: {total_revenue:,.2f} VND")
print("=== VERIFICATION COMPLETED ===")
