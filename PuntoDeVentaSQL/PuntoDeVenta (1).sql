IF DB_ID('PuntoDeVenta') IS NULL CREATE DATABASE PuntoDeVenta;
GO
USE PuntoDeVenta;
GO

DROP VIEW IF EXISTS VentasPorDia;
DROP VIEW IF EXISTS ProductosMasVendidos;
DROP VIEW IF EXISTS VentasPorCajero;
IF OBJECT_ID('FacturaDetalle') IS NOT NULL DROP TABLE FacturaDetalle;
IF OBJECT_ID('Facturas')       IS NOT NULL DROP TABLE Facturas;
IF OBJECT_ID('Productos')      IS NOT NULL DROP TABLE Productos;
IF OBJECT_ID('Usuarios')       IS NOT NULL DROP TABLE Usuarios;
GO

CREATE TABLE Usuarios (
    Usuario  VARCHAR(50)  NOT NULL PRIMARY KEY,
    Nombre   VARCHAR(100) NOT NULL,
    Clave    CHAR(64)     NOT NULL,              
    Rol      VARCHAR(10)  NOT NULL CHECK (Rol IN ('admin','cajero'))
);

CREATE TABLE Productos (
    Codigo      VARCHAR(30)   NOT NULL PRIMARY KEY,
    Nombre      VARCHAR(150)  NOT NULL,
    Precio      DECIMAL(18,2) NOT NULL CHECK (Precio >= 0),
    Existencia  INT           NOT NULL DEFAULT 0 CHECK (Existencia >= 0)
);

CREATE TABLE Facturas (
    No          INT IDENTITY(1,1) PRIMARY KEY,
    Fecha       DATETIME      NOT NULL DEFAULT GETDATE(),
    Cajero      VARCHAR(100)  NOT NULL,
    Cliente     VARCHAR(150)  NOT NULL DEFAULT 'Consumidor final',
    RncCedula   VARCHAR(30)   NULL,
    FormaPago   VARCHAR(20)   NOT NULL,           -- Efectivo, Tarjeta, Transferencia
    Subtotal    DECIMAL(18,2) NOT NULL,
    Descuento   DECIMAL(18,2) NOT NULL DEFAULT 0,
    Itbis       DECIMAL(18,2) NOT NULL,           -- 18 %
    Total       DECIMAL(18,2) NOT NULL,
    Recibido    DECIMAL(18,2) NOT NULL
);

CREATE TABLE FacturaDetalle (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    FacturaNo  INT           NOT NULL REFERENCES Facturas(No),
    Codigo     VARCHAR(30)   NOT NULL,
    Articulo   VARCHAR(150)  NOT NULL,
    Precio     DECIMAL(18,2) NOT NULL,
    Cantidad   INT           NOT NULL CHECK (Cantidad > 0)
);
GO

-- ---------- Datos iniciales ----------
-- Usuario admin / contrasena admin123
INSERT INTO Usuarios (Usuario, Nombre, Clave, Rol)
VALUES ('admin', 'Administrador', '8D109B2BCA77AED2E4098C0FF5EC16089334C8C43121B4C61B4208095D0CB126', 'admin');

INSERT INTO Productos (Codigo, Nombre, Precio, Existencia) VALUES
 ('1001', 'Agua 500ml',    35.00, 100),
 ('1002', 'Refresco 20oz', 60.00,  80),
 ('1003', 'Pan de agua',   10.00, 200),
 ('1004', 'Arroz 1lb',     38.00, 150),
 ('1005', 'Cafe 8oz',     185.00,  40);
GO

-- ---------- Vistas para reportes ----------
CREATE VIEW VentasPorDia AS
SELECT CAST(Fecha AS DATE) AS Dia, COUNT(*) AS Facturas, SUM(Total) AS TotalVendido
FROM Facturas
GROUP BY CAST(Fecha AS DATE);
GO

CREATE VIEW ProductosMasVendidos AS
SELECT Codigo, Articulo, SUM(Cantidad) AS Unidades, SUM(Precio * Cantidad) AS Importe
FROM FacturaDetalle
GROUP BY Codigo, Articulo;
GO

CREATE VIEW VentasPorCajero AS
SELECT Cajero, COUNT(*) AS Facturas, SUM(Subtotal) AS Subtotal, SUM(Descuento) AS Descuento,
       SUM(Itbis) AS Itbis, SUM(Total) AS Total
FROM Facturas
GROUP BY Cajero;
GO
