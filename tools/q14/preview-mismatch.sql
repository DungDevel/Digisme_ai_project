-- preview-mismatch.sql: Preview mismatched products between Products.CurrentStock and SUM(WarehouseStocks.Stock)
WITH ProductStockSummary AS (
    SELECT
        p."Id" AS "ProductId",
        p."Barcode",
        p."Name",
        p."CurrentStock",
        COALESCE(SUM(ws."Stock"), 0) AS "TotalWarehouseStock",
        p."CurrentStock" - COALESCE(SUM(ws."Stock"), 0) AS "Difference"
    FROM "Products" p
    LEFT JOIN "WarehouseStocks" ws ON ws."ProductId" = p."Id"
    GROUP BY p."Id", p."Barcode", p."Name", p."CurrentStock"
    HAVING p."CurrentStock" <> COALESCE(SUM(ws."Stock"), 0)
)
SELECT
    'OVER_STOCK' AS "GroupType",
    "ProductId",
    "Barcode",
    "Name",
    "CurrentStock",
    "TotalWarehouseStock",
    "Difference"
FROM ProductStockSummary
WHERE "Difference" > 0

UNION ALL

SELECT
    'UNDER_STOCK' AS "GroupType",
    "ProductId",
    "Barcode",
    "Name",
    "CurrentStock",
    "TotalWarehouseStock",
    "Difference"
FROM ProductStockSummary
WHERE "Difference" < 0;
