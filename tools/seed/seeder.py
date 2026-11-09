import json
import random
import uuid
import hashlib
import base64
from datetime import datetime, timedelta, timezone
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path
import psycopg
from psycopg import sql
from faker import Faker

BASE_DIR = Path(__file__).resolve().parent
CONFIG_PATH = BASE_DIR / "seed.config.json"
LOG_PATH = BASE_DIR / "seed_log.txt"

def load_config():
    with open(CONFIG_PATH, "r", encoding="utf-8-sig") as f:
        return json.load(f)

def hash_password(password: str) -> str:
    salt = "DigiSME.pos.api.salt.v1".encode("utf-8")
    dk = hashlib.pbkdf2_hmac("sha256", password.encode("utf-8"), salt, 300_000, dklen=32)
    return base64.b64encode(dk).decode("utf-8")

class DigiSMESeeder:
    def __init__(self, config, dry_run=False):
        self.config = config
        self.dry_run = dry_run
        self.fake = Faker('vi_VN')
        random.seed(config["seed_value"])

        self.conn_str = "host=postgres port=5432 dbname=digism_pos user=postgres password=postgres"
        self.conn = None

        self.ids = {
            "users": [], "categories": [], "products": [], "suppliers": [],
            "warehouses": [], "customers": [], "shifts": []
        }

    def connect(self):
        if not self.dry_run:
            self.conn = psycopg.connect(self.conn_str, autocommit=False)

    def log(self, message):
        print(f"[{datetime.now().strftime('%H:%M:%S')}] {message}")
        with open(LOG_PATH, "a", encoding="utf-8") as f:
            f.write(f"[{datetime.now().strftime('%H:%M:%S')}] {message}\n")

    def bulk_insert(self, table, columns, data):
        if not data: return
        if self.dry_run:
            self.log(f"[Dry Run] Would insert {len(data)} rows into {table}")
            return

        with self.conn.cursor() as cur:
            placeholders = sql.SQL(", ").join([sql.Placeholder()] * len(columns))
            query = sql.SQL("INSERT INTO {table} ({cols}) VALUES ({placeholders})").format(
                table=sql.Identifier(table),
                cols=sql.SQL(", ").join(map(sql.Identifier, columns)),
                placeholders=placeholders
            )
            cur.executemany(query, data)

    def get_all_ids(self, table):
        if self.dry_run: return []
        with self.conn.cursor() as cur:
            cur.execute(sql.SQL("SELECT \"Id\" FROM {table}").format(table=sql.Identifier(table)))
            return [row[0] for row in cur.fetchall()]

    def generate_timestamp(self, start_date, end_date):
        start = datetime.strptime(start_date, "%Y-%m-%d")
        end = datetime.strptime(end_date, "%Y-%m-%d")
        delta = end - start
        random_days = random.randrange(delta.days)
        date = start + timedelta(days=random_days)

        hour_probs = [0.01]*24
        for h in range(11, 14): hour_probs[h] = 0.15
        for h in range(17, 21): hour_probs[h] = 0.20

        hour = random.choices(range(24), weights=hour_probs)[0]
        return datetime(date.year, date.month, date.day, hour, random.randint(0, 59), random.randint(0, 59))

    def seed_roles_and_users(self):
        self.log("Seeding Users...")
        roles = [("Admin", 1), ("Store Manager", 2), ("Accountant", 3), ("Cashier", 4)]
        user_data = []
        now = datetime.now(timezone.utc)
        hashed_pwd = hash_password("hashed_password_123")
        for role_name, role_id in roles:
            pos_role = 1 if role_id <= 2 else 0
            user_data.append((f"{role_name.lower()}_test", hashed_pwd, f"{role_name} Test", f"test_{role_name.lower()}@example.com", True, pos_role, now))

        for i in range(self.config["entities"]["users"] - len(roles)):
            user_data.append((f"user_{i}", hashed_pwd, self.fake.name(), self.fake.email(), True, 0, now))

        self.bulk_insert("Users", ["Username", "PasswordHash", "FullName", "Email", "IsActive", "Role", "CreatedAt"], user_data)
        self.ids["users"] = self.get_all_ids("Users")

    def seed_catalog(self):
        self.log("Seeding Catalog...")
        cats = [[f"Category {i}", True] for i in range(self.config["entities"]["categories"])]
        self.bulk_insert("Categories", ["Name", "IsActive"], cats)
        cat_ids = self.get_all_ids("Categories")

        now = datetime.now(timezone.utc)
        products = []
        for i in range(self.config["entities"]["products"]):
            retail = Decimal(random.randrange(5000, 500000)).quantize(Decimal("1"), rounding=ROUND_HALF_UP)
            products.append((
                self.fake.word(), random.choice(cat_ids), f"893{random.randint(1000000000, 9999999999)}",
                retail * Decimal("0.7"), retail, retail * Decimal("0.9"), retail * Decimal("0.8"),
                random.randint(1, 100), Decimal("0"), random.choice([True, False]), True, now
            ))
        self.bulk_insert("Products", ["Name", "CategoryId", "Barcode", "CostPrice", "RetailPrice", "WholesalePrice", "MemberPrice", "MinStock", "CurrentStock", "IsWeighed", "IsActive", "CreatedAt"], products)
        self.ids["products"] = self.get_all_ids("Products")

    def seed_customers(self):
        self.log("Seeding Customers...")
        cust_data = []
        now = datetime.now(timezone.utc)
        for i in range(self.config["entities"]["customers"]):
            phone = f"09{i+1:08d}"
            cust_data.append((self.fake.name(), phone, self.fake.address(), random.choice([True, False]), True, now))
        self.bulk_insert("Customers", ["Name", "Phone", "Address", "IsVip", "IsActive", "CreatedAt"], cust_data)
        self.ids["customers"] = self.get_all_ids("Customers")

    def seed_inventory_base(self):
        self.log("Seeding Inventory Base...")
        now = datetime.now(timezone.utc)
        supps = []
        for i in range(self.config["entities"]["suppliers"]):
            supps.append((f"NCC-{i:05d}", self.fake.company(), True, now))
        self.bulk_insert("Suppliers", ["Code", "Name", "IsActive", "CreatedAt"], supps)

        whs = []
        for i in range(self.config["entities"]["stores"]):
            is_default = (i == 0)
            whs.append((f"WH-{i:05d}", f"Warehouse {i}", is_default, True, now))
        self.bulk_insert("Warehouses", ["Code", "Name", "IsDefault", "IsActive", "CreatedAt"], whs)
        self.ids["warehouses"] = self.get_all_ids("Warehouses")

        stock_data = []
        for p_id in self.ids["products"]:
            for w_id in self.ids["warehouses"]:
                stock_data.append((p_id, w_id, random.randint(10, 1000), now))
        self.bulk_insert("WarehouseStocks", ["ProductId", "WarehouseId", "Stock", "UpdatedAt"], stock_data)

    def seed_sales_and_logs(self):
        self.log("Seeding Sales and Logs...")
        shifts = []
        for i in range(self.config["entities"]["shifts"]):
            shifts.append((random.choice(self.ids["users"]), self.generate_timestamp(self.config["start_date"], self.config["end_date"]), 0, Decimal("1000000")))
        self.bulk_insert("Shifts", ["UserId", "StartedAt", "Status", "OpeningCash"], shifts)
        shift_ids = self.get_all_ids("Shifts")
        wh_ids = self.ids["warehouses"]

        order_items, stock_ledger, pos_logs = [], [], []
        start_date, end_date = self.config["start_date"], self.config["end_date"]

        for i in range(self.config["entities"]["orders"]):
            cust_id = random.choice(self.ids["customers"])
            user_id = random.choice(self.ids["users"])
            shift_id = random.choice(shift_ids)
            warehouse_id = random.choice(wh_ids)
            timestamp = self.generate_timestamp(start_date, end_date)
            code = f"INV-{timestamp.strftime('%Y%m%d')}-{i+1:04d}"
            subtotal = Decimal("0")

            items_for_order = []
            for _ in range(random.randint(1, 5)):
                p_id = random.choice(self.ids["products"])
                qty = Decimal(random.randint(1, 3))
                price = Decimal(random.randrange(5000, 50000))
                line_total = price * qty
                subtotal += line_total
                items_for_order.append((p_id, qty, price, line_total))
                stock_ledger.append((p_id, warehouse_id, 0, -qty, Decimal("0"), timestamp))

            total = subtotal
            paid = total

            # Insert Order and get generated Id
            with self.conn.cursor() as cur:
                cur.execute(
                    """
                    INSERT INTO "Orders" ("Code", "CashierId", "CustomerId", "ShiftId", "Subtotal", "Discount", "SurchargeTotal", "Vat", "ShippingFee", "Total", "PaymentMethod", "PaidAmount", "Change", "DeliveryMethod", "Status", "CreatedAt", "CompletedAt")
                    VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
                    RETURNING "Id";
                    """,
                    (code, user_id, cust_id, shift_id, subtotal, Decimal("0"), Decimal("0"), Decimal("0"), Decimal("0"), total, 0, paid, Decimal("0"), "instore", 1, timestamp, timestamp)
                )
                order_id = cur.fetchone()[0]

            for p_id, qty, price, line_total in items_for_order:
                order_items.append((order_id, p_id, qty, price, Decimal("0"), Decimal("0"), Decimal("0"), line_total, Decimal("0"), False))

            pos_logs.append(("ORDER_CREATED", user_id, "order", code, json.dumps({"total": float(total)}), timestamp))

            if len(order_items) >= 5000:
                self.bulk_insert("OrderItems", ["OrderId", "ProductId", "Qty", "UnitPrice", "LineDiscount", "Vat", "Surcharge", "Total", "ReturnedQty", "Cancelled"], order_items)
                self.bulk_insert("StockLedger", ["ProductId", "WarehouseId", "RefType", "Qty", "Balance", "CreatedAt"], stock_ledger)
                self.bulk_insert("PosLogs", ["Action", "UserId", "EntityType", "EntityId", "DetailJson", "CreatedAt"], pos_logs)
                order_items, stock_ledger, pos_logs = [], [], []

        self.bulk_insert("OrderItems", ["OrderId", "ProductId", "Qty", "UnitPrice", "LineDiscount", "Vat", "Surcharge", "Total", "ReturnedQty", "Cancelled"], order_items)
        self.bulk_insert("StockLedger", ["ProductId", "WarehouseId", "RefType", "Qty", "Balance", "CreatedAt"], stock_ledger)
        self.bulk_insert("PosLogs", ["Action", "UserId", "EntityType", "EntityId", "DetailJson", "CreatedAt"], pos_logs)

    def run(self):
        try:
            self.connect()
            self.seed_roles_and_users()
            self.seed_catalog()
            self.seed_customers()
            self.seed_inventory_base()
            self.seed_sales_and_logs()
            if not self.dry_run: self.conn.commit()
            self.log("Seeding completed successfully!")
        except Exception as e:
            if self.conn: self.conn.rollback()
            self.log(f"ERROR: {str(e)}")
            raise e
        finally:
            if self.conn: self.conn.close()

if __name__ == "__main__":
    import sys
    dry = "--dry-run" in sys.argv
    cfg = load_config()
    seeder = DigiSMESeeder(cfg, dry_run=dry)
    seeder.run()
