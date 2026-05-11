# HST Secure Dashboard - C++ Qt Premium

Bem-vindo ao projeto do Dashboard Corporativo HST. Este projeto foi desenvolvido inteiramente em **C++** utilizando o framework **Qt6**, sendo focado em alta performance, código extremamente limpo (sem uso de arquivos `.ui` de arrastar e soltar) e uma interface gráfica "Dark Premium" com renderização nativa.

Este arquivo irá guiar você em como estudar o código, executar no seu computador e também em como gerar uma versão final (Release) para rodar em **qualquer computador Windows**, mesmo sem o Qt instalado.

---

## 📚 Arquitetura e Funcionalidades

Abra o arquivo `main.cpp` na sua IDE (como VSCode). O código foi totalmente comentado separando o projeto em blocos principais:

1. **Dashboard & Gráficos**: Cards de métricas e Gráficos de linha/barras desenhados "na mão" com `QPainter` e `QPropertyAnimation`.
2. **Exportação Excel (CSV)**: A tabela de "Transactions Log" usa bibliotecas nativas de I/O do C++ (`QFile` e `QTextStream`) para exportar dados para Excel (.csv).
3. **Chat de Rede (Boost.Asio)**: Uma implementação real do padrão da indústria (Standalone Asio) conectando Computadores na mesma rede. Usa `std::thread` para rodar o `io_context` em background, garantindo uma arquitetura assíncrona que não trava a interface visual do Qt. O script baixa o Asio automaticamente pelo CMake `FetchContent`.
4. **Interface Glassmorphism**: Utilização de sombras (`QGraphicsDropShadowEffect`) e transparências na aba lateral para misturar imagens de fundo.

---

## 🛠️ Guia de Execução Local (Desenvolvimento)

Sempre que você alterar o `main.cpp` e quiser testar no seu próprio PC:

1. Abra o **PowerShell** ou o terminal integrado do VSCode.
2. Navegue até a pasta do projeto e rode o script de automação:
   ```powershell
   .\rodar.ps1
   ```
   *Este script apagará o cache antigo, recriará os binários usando o CMake/MinGW e abrirá o aplicativo automaticamente.*

---

## 🚀 Guia de Deploy e Distribuição (Release de Produção)

Para que o aplicativo rode em **outro computador** (da sua casa ou empresa) que **não possui o Qt** ou ferramentas de programação instaladas, você não pode simplesmente copiar o `.exe`. É necessário fazer o empacotamento com as DLLs.

Para facilitar, criamos um script que faz todo o trabalho duro automaticamente usando a ferramenta oficial `windeployqt`.

### Passo a Passo:

1. Abra o **PowerShell** na pasta do projeto.
2. Execute o script de deploy:
   ```powershell
   .\deploy.ps1
   ```
3. **O que o script faz por trás dos panos?**
   - Compila o seu código usando a flag `-DCMAKE_BUILD_TYPE=Release`, que otimiza o aplicativo e remove códigos de debug, deixando-o extremamente rápido.
   - Cria uma nova pasta chamada `dist`.
   - Copia o seu `DashboardProject.exe` e as imagens de fundo para dentro dela.
   - Executa a ferramenta `windeployqt`. Ela escaneia o seu `.exe`, descobre quais bibliotecas do Qt ele utiliza e copia automaticamente todas as DLLs para a pasta `dist`.

### Apresentando o Resultado 🏆

Após o script finalizar, a pasta **`dist`** é a sua "Pasta de Produção"!
- Copie a pasta `dist` inteira para um **Pen Drive** ou mande para o seu outro PC pela rede.
- No seu outro computador, abra a pasta copiada e dê um duplo clique em `DashboardProject.exe`.
- Aproveite e teste a aba **Asio Network Chat** para conectar o Servidor (PC1) ao Cliente (PC2) via IP local!
