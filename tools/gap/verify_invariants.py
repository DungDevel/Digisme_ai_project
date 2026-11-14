import psycopg2
import sys

connection_string = "host=localhost port=5432 dbname=digism_pos user=pos_user password=pos_pass"

try:
    conn = psycopg2.connect(connection_string)
    cur = conn.cursor()
    cur.execute("SELECT column_name FROM information_schema.columns WHERE table_name = 'Debts';")
    print("Columns in Debts:", [r[0] for r in cur.fetchall()])
    cur.close()
    conn.close()
except Exception as e:
    print(f"Error: {e}")
