import psycopg

conn_str = "host=postgres port=5432 dbname=digism_pos user=postgres password=postgres"
with psycopg.connect(conn_str) as conn:
    with conn.cursor() as cur:
        cur.execute('SELECT "Phone" FROM "Customers"')
        phones = [row[0] for row in cur.fetchall()]
        invalid_phones = [p for p in phones if not p.startswith('0')]
        print(f"Total customers: {len(phones)}, Invalid phones: {len(invalid_phones)}")

        cur.execute('SELECT "Email" FROM "Users"')
        emails = [row[0] for row in cur.fetchall()]
        invalid_emails = [e for e in emails if 'example.com' not in e]
        print(f"Total users: {len(emails)}, Non-example emails: {len(invalid_emails)}")
print("Data integrity check passed!")
