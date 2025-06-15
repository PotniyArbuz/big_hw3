# Разработка интернет-магазина

## 1. Назначение микросервисов

| Микросервис       | Ответственность                                                                 |
|-------------------|---------------------------------------------------------------------------------|
| **OrdersService** | Создание заказов, хранение их статуса. Публикует событие `PaymentRequested`.    |
| **PaymentsService** | Учёт денег на счёте пользователя. Подписывается на `PaymentRequested`, списывает средства, публикует `PaymentProcessed`. |

- **Связь между сервисами**: организована через **RabbitMQ** с использованием библиотеки **MassTransit**.
- **Хранение данных**: **PostgreSQL** с использованием **Entity Framework Core**.

## 2. Архитектурная диаграмма

```scss
   ┌──────────────┐       PaymentRequested        ┌────────────────┐
   │ OrdersService│ ────────────────────────────▶ | PaymentsService│
   └──────────────┘                               └────────────────┘
        ▲   │                                         │
        │   └──── PaymentProcessed ───────────────────┘
        │
REST API (Swagger)
```

## 3. Технологический стек

- **ASP.NET Core Minimal API**: контроллеры для REST API.
- **Entity Framework Core**: доступ к PostgreSQL.
- **MassTransit 8.x**: абстракция поверх RabbitMQ, реализация паттернов Outbox/Inbox.
- **Docker Compose**: запуск инфраструктуры.
- **Swagger UI**: интерактивное тестирование методов.

## 4. Основные сценарии

1. **Создание пользователя и пополнение счёта**:
   - `POST /accounts` — создание аккаунта.
   - `POST /accounts/deposit` — пополнение счёта.

2. **Создание заказа**:
   - `POST /orders` — возвращает `202 Accepted`.

3. **Фоновая обработка платежа**:
   - `PaymentsService` обрабатывает событие `PaymentRequested`, списывает средства, публикует `PaymentProcessed`.

4. **Обновление статуса заказа**:
   - `OrdersService` получает `PaymentProcessed`, обновляет статус заказа на `Paid` или `Failed`.

## 5. Запуск проекта

Перейти в папку проекта (shop_microservices) и собрать решение:

```bash
docker compose up --build
# docker compose down -v        # убрать старые тома
```

- **Swagger UI**:
  - OrdersService: `http://localhost:5001/swagger`
  - PaymentsService: `http://localhost:5002/swagger`
- **RabbitMQ UI**:
  - `http://localhost:15672`

## 6. Пример работы с одним и тем же userId (допустим, 11111111-1111-1111-1111-111111111111)

1. Создание аккаунта:
   ```http
   POST /accounts?userId=<GUID>
   ```

2. Пополнение счёта:
   ```http
   POST /accounts/deposit

   {
     "userId": "<GUID>",
     "amount": 100
   }
   ```
   
3. Создание заказа:
   ```http
   POST /orders

   {
     "userId": "<GUID>",
     "amount": 60
   }
   ```

4. Проверка статуса заказа:
   ```http
   GET /orders/<orderId>
   ```

5. Проверка баланса:
   ```http
   GET /accounts/<userId>
   ```
