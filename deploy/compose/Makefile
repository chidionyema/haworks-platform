.PHONY: up down build logs status clean

up:
	docker-compose up -d

down:
	docker-compose down

build:
	docker-compose build

logs:
	docker-compose logs -f

status:
	docker-compose ps

clean:
	docker-compose down -v
	rm -rf vault-creds/*
