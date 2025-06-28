# REST_API_Server
게임 서버를 위한 REST API 서버 설계 및 구축

# Directory

|폴더명|설명|
|:--:|:--:|
|REST|소스코드|
|query|DB생성쿼리문|
|img|리드미 이미지|

## 흐름도

![](https://velog.velcdn.com/images/owljun/post/33f65c2b-bb6e-4d67-8f19-cd56b780416b/image.png)


## 목표

클라이언트를 내 생각대로 쭉쭉 만들어내는 재주가 나에게는 없기에... <br>
`로그인UI` - `월드씬` 두 씬으로 구성된 간단한 클라이언트를 Unity로 만들어 사용할 생각이다.


## 구현

- ASP.NET Core
   - C# 베이스의 비교적 익숙함으로 인해 채택
   - ~Minimal API 채택~
      - ASP.NET Core 사용이 처음 (학습 필요)
      - 실시간성 요구되는 기능 및 게임관련 로직은 대부분 TCP 서버가 책임 
      - UI가 따로 필요하지 않기에 MVC모델 보다 개발 생산성 월등히 높음
   - Controller 매핑 방식으로 리팩토링
      - 초기 학습에는 Minimal API가 직관적이고 좋았으나, 확장성을 염두해두고 싶었음.
      - 어느정도 익숙해진 지금, 더 기능을 추가하기 전에 리팩토링 시작 (250609) 
      
- RESTful API
   - POST /Login , /Register 구현 (로그인 및 회원가입로직)
   - POST GET PUT DELETE /inventory (인벤토리 관련 요청)

- Redis 연동
   - StackExchange.Redis
   - Redis CRUD 클래스 생성
   
- DB 연동
   - Pomelo.EntityFrameworkCore.MySql 
   - Pomelo가 호환성 면이나 LINQ 지원 및 경량&최적화가 오라클 공식프레임워크보다 좋아서 채택하게 됨.
   
   
## DB 설계

![](https://velog.velcdn.com/images/owljun/post/d93d859e-dd74-470b-ac95-1356e8605e2f/image.png)



1. **유저(Users)**
	- 기본적인 로그인/인증/식별 정보 보유
	- ISSUE : BCrypt 패스워드 해싱시 데이터 길이가 길어지는 관계로 password VARCHAR(100) 으로 수정

2. **아이템(Items)**
	- 고유 아이템 정보 정의 (이름, 설명, 분류, 희귀도 등)

3. **인벤토리(Inventory)**
    - 유저가 보유한 아이템 수량, 획득 시점 등 저장
    - 중간 테이블 역할 (N:M 관계 분리)
    - User_Id, Item_Id → PK : 조합 , **Inventory_Id 삭제**

- 유저 - 아이템 의 관계는 N : M
- 중간에 인벤토리 테이블을 추가하여 1 : N , M: 1 로 분리

## 보안

- JWT
과거 로그인 상태 관리를 학습할 땐 단순히 "로그인 → 세션 유지" 흐름만을 고려했지만,
이번엔 클라이언트 조작이나 API 위·변조 같은 보안 이슈까지 고려

   - REST 서버 구조상, 인증 상태를 유지할 세션이 없음
   - 각 API 요청 시 유저의 신원을 확실하게 증명할 수단이 필요
   - 토큰 기반 인증(JWT)은 매 요청마다 인증 정보가 포함되므로 완전히 stateless한 구조에 적합
   
- BCrypt
비밀번호는 절대 평문으로 저장되면 안 되는 민감 정보이며,
만약 DB가 유출되었을 경우를 가정하면 단순 해싱(SHA256 등)만으로도 공격자에게 취약할 수 있다.
   - 단방향 해싱 + Salt 자동 포함 (입력값이 같아도 해시값이 매번 달라, 강력한 보안)
   - 느린 해시 연산 속도로 인해 해커 입장에서 해킹시도에 시간이 많이 걸림
   - 라이브러리 자체가 검증되어있음.


## 테스트

- Swagger

> Nuget : Swashbuckle.AspNetCore

현재 클라이언트 구현이 안 된 상황에서 `insomnia` , 크롬 확장프로그램 등 여러가지 REST API 테스트 툴 후보들이 있었으나, Swagger의 강력한 자동 문서화, 즉시 테스트 가능 및 토큰인증 간편, ASP.NET Core 공식 지원 ... 장점이 매우 커서 선택하게 된 툴. 매우 만족스럽다.

- API 명세서 (/inventory : 구현중)
  
![](https://velog.velcdn.com/images/owljun/post/52d36b7c-e985-4832-99d5-1644c40084a3/image.png)

---

- 회원가입요청 테스트
  
![](https://velog.velcdn.com/images/owljun/post/d3416e95-4887-4f57-93f5-f4b5a10ba28a/image.png)

---

- 로그인요청 테스트
  
![](https://velog.velcdn.com/images/owljun/post/3d9809f8-d1b6-45c3-9188-beb87aae82f2/image.png)


## 남은 개발일정

- [x] REDIS 연동
- [x] 중복 로그인 방지
- inventory 관련 API 메서드 구현
- 캐시 동기화 관련 규칙
