# Slugcat Companion para Windows

Um companion de desktop em janela transparente e sempre visível. O Slugcat começa perto do canto inferior direito, anda sozinho em pequenos intervalos e pula quando você clica nele. Arraste o personagem para mover a janela. O menu do ícone na bandeja do sistema permite dar um pulo ou sair; um duplo clique no ícone esconde ou mostra o companion.

## Executar

Abra `SlugcatCompanion-v15.exe`. Mantenha `assets\player` ao lado do executável; as imagens são partes oficiais do atlas `rainWorld` da instalação local de Rain World.

## Recompilar

Abra PowerShell nesta pasta e execute:

```powershell
.
build.ps1
```

O código usa o compilador do .NET Framework incluído no Windows.

## Física e renderização

O app usa os dois `BodyChunk` do Survivor (raios 9 e 8, conexão normal de 17), gravidade 0,9, atrito no ar 0,999, quique 0,1 e atrito de superfície 0,5. 

### v15 - MEGA UPDATE ESTÉTICA:
- **Braços dinâmicos**: 13 frames (PlayerArm0-12) com balanço natural durante caminhada
- **Cabeça rotativa**: 18 orientações (HeadA0-17) seguem direção do movimento
- **Expressões faciais**: 9 frames (FaceA0-8) com sistema automático de piscadas
- **Olhar vertical**: Cabeça olha pra cima ao pular e pra baixo ao cair
- **Ordem de renderização**: Braço traseiro → corpo → pernas → braço frontal → cabeça → rosto

### Estrutura física:
- Rig com 3 pivôs: cintura, barriga articulada e pescoço
- Cauda com 4 segmentos físicos, conexões alongadas em 30%
- Contorno interpolado (Catmull-Rom) e desenhado mais largo para silhueta suave
- Postura limitada na caminhada para manter personagem em pé no piso plano

A simulação avança a 40 atualizações por segundo. A janela substitui as colisões com salas e terrenos do jogo por um piso plano, e não carrega o runtime inteiro da Unity nem o código de estados de movimento do `Player`.
