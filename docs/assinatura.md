# Assinatura digital do instalador

## Por que

No Windows 11 com o **Controle Inteligente de Aplicativos** (Smart App Control) ligado, programa sem assinatura
digital é bloqueado sem botão para continuar ("não conseguimos verificar seu fornecedor"). A Microsoft não oferece
como liberar um app só: ou o programa vem assinado, ou a pessoa desliga a proteção. E não basta assinar o instalador:
o controle confere cada `.exe` e `.dll` que o Windows carrega, inclusive depois de instalado.

A assinatura é pedida à [SignPath Foundation](https://signpath.org), que assina de graça projetos de código aberto.
A política pública fica no site: [Code signing policy](https://graceforge.com.br/projetos/achievements-mp-code-signing-policy).

## O que é assinado

Das 412 peças executáveis do pacote da 0.9.0, 398 já vêm assinadas pela Microsoft (.NET e WPF) ou pelos autores.
Das 14 restantes:

| Arquivo | Origem | Assinado? |
| --- | --- | --- |
| `AchievementsMP.exe`, `AchievementsMP.dll`, `.Core`, `.Data`, `.Steam`, `.Sync` | este repositório | sim |
| `Squirrel.exe` (vira o `Update.exe`) e `Achievements MP_ExecutionStub.exe` (vira o `Achievements MP.exe` da raiz, que os atalhos abrem) | gerados pelo vpk a partir do Velopack (MIT) | sim, se a SignPath aceitar (ver abaixo) |
| `AchievementsMP.App-win-Setup.exe` | gerado pelo vpk: instalador do Velopack com o pacote dentro | sim |
| `Dapper.dll`, `SQLitePCLRaw.*.dll`, `e_sqlite3.dll`, `Velopack.dll` | bibliotecas de terceiros | não: a SignPath só assina código do próprio projeto |

**Ponto em aberto:** os binários do Velopack vêm sem assinatura de origem, e as regras da SignPath não permitem
assinar código de terceiros. Sem assinatura, o atualizador e o lançador dos atalhos seriam bloqueados pelo controle
do Windows (o app não abriria pelo atalho, não atualizaria nem desinstalaria). Isso precisa ser perguntado à SignPath
na inscrição. As bibliotecas de terceiros são distribuídas idênticas em milhares de apps e a Microsoft provavelmente
já as conhece, mas só um teste num PC com o controle ligado confirma.

## Como funciona no workflow Release

A SignPath assina num servidor dela e cada versão precisa de aprovação manual, então o vpk monta o pacote duas vezes:

1. **1ª passada:** o vpk chama `.github/assinatura/vpk-assinar.ps1 coletar` com o que assinaria; o script separa os
   arquivos da lista `.github/assinatura/arquivos.txt`.
2. **SignPath, 1ª aprovação:** esses arquivos vão para a SignPath (configuração de artefato `pacote`) e voltam assinados.
3. **2ª passada:** o vpk monta o pacote de verdade; o script troca cada arquivo pelo assinado, só se for byte a byte
   igual ao da 1ª passada.
4. **SignPath, 2ª aprovação:** o `Setup.exe`, que embute o pacote, vai para a SignPath (configuração `instalador`).
5. O workflow confere no Windows que as assinaturas valem, instala, confere de novo o que ficou instalado, atualiza,
   desinstala e só então envia às Releases.

O build roda só em máquinas do GitHub, a partir do código deste repositório, e a SignPath confere de qual execução
veio cada pedido.

**Ensaio:** Actions → Release → Run workflow → assinatura = `ensaio` faz tudo isso com um certificado de teste
criado na hora, sem a SignPath e sem enviar nada às Releases. Serve para conferir o caminho depois de mexer no
empacotamento.

## Configurar depois que a SignPath aprovar

Na SignPath:

1. Projeto com o slug `achievements-mp`, ligado ao repositório e ao Trusted Build System **GitHub.com**.
2. Política de assinatura `release-signing`, com aprovação manual.
3. Duas configurações de artefato, com o conteúdo destes arquivos: `pacote` ←
   [`signpath-pacote.xml`](../.github/assinatura/signpath-pacote.xml) e `instalador` ←
   [`signpath-instalador.xml`](../.github/assinatura/signpath-instalador.xml).
4. Um token de API de um usuário com permissão de enviar pedidos.

No GitHub (Settings → Secrets and variables → Actions):

- Secret `SIGNPATH_API_TOKEN` com o token.
- Variável `SIGNPATH_ORGANIZATION_ID` com o ID da organização. Com ela, o Release passa a assinar sozinho.
- Só se os nomes forem outros: variáveis `SIGNPATH_PROJECT_SLUG` e `SIGNPATH_POLICY_SLUG`.

A cada versão, o workflow para duas vezes esperando a aprovação no painel da SignPath (até 2 horas cada).

## Papéis

| Papel | Quem |
| --- | --- |
| Autor, revisor e aprovador das assinaturas | Pablo ([PABLOzxk](https://github.com/PABLOzxk) no GitHub) |

Todo mundo com acesso ao repositório ou à SignPath usa verificação em duas etapas. Contribuições de fora entram só
por pull request revisado.
