using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using ServerPanel.Components.Pages.Players.Modals;

namespace ServerPanel.Tests;

public class ReasonModalTests : TestContext
{
    // ── Visibilidad ──────────────────────────────────────────────────────────

    [Fact]
    public void No_renderiza_backdrop_cuando_Show_es_false()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, false));

        Assert.Empty(cut.FindAll(".modal-backdrop"));
    }

    [Fact]
    public void Renderiza_backdrop_cuando_Show_es_true()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true));

        Assert.NotEmpty(cut.FindAll(".modal-backdrop"));
    }

    [Fact]
    public void Muestra_titulo_correctamente()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "KICK"));

        Assert.Contains("KICK", cut.Find(".modal-title").TextContent);
    }

    // ── Campo motivo ─────────────────────────────────────────────────────────

    [Fact]
    public void Muestra_input_reason_cuando_ShowReason_true()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, true));

        Assert.NotEmpty(cut.FindAll("#reasonInput"));
    }

    [Fact]
    public void Oculta_input_reason_cuando_ShowReason_false()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, false));

        Assert.Empty(cut.FindAll("#reasonInput"));
    }

    // ── Campo duración ───────────────────────────────────────────────────────

    [Fact]
    public void Muestra_input_duration_cuando_ShowDuration_true()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowDuration, true));

        Assert.NotEmpty(cut.FindAll("#durationInput"));
    }

    [Fact]
    public void Oculta_input_duration_cuando_ShowDuration_false()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowDuration, false));

        Assert.Empty(cut.FindAll("#durationInput"));
    }

    // ── Estado del botón Confirmar ───────────────────────────────────────────

    [Fact]
    public void Confirmar_desactivado_cuando_ShowReason_true_y_reason_vacia()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, true)
            .Add(m => m.Reason, ""));

        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }

    [Fact]
    public void Confirmar_activo_cuando_ShowReason_false()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, false));

        Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }

    [Fact]
    public void Confirmar_se_activa_al_escribir_un_motivo()
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, true)
            .Add(m => m.Reason, ""));

        cut.Find("#reasonInput").Input("griefing");

        Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }

    // ── Callbacks al confirmar ───────────────────────────────────────────────

    [Fact]
    public async Task Confirmar_invoca_OnConfirm()
    {
        var disparado = false;

        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, false)
            .Add(m => m.OnConfirm, EventCallback.Factory.Create(this, () => disparado = true)));

        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        Assert.True(disparado);
    }

    [Fact]
    public async Task Confirmar_envia_motivo_via_ReasonChanged()
    {
        var motivoRecibido = "";

        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, true)
            .Add(m => m.ReasonChanged, EventCallback.Factory.Create<string>(this, v => motivoRecibido = v)));

        cut.Find("#reasonInput").Input("spam");
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        Assert.Equal("spam", motivoRecibido);
    }

    [Fact]
    public async Task Confirmar_envia_duracion_via_DurationMinutesChanged()
    {
        var duracionRecibida = 0;

        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, false)
            .Add(m => m.ShowDuration, true)
            .Add(m => m.DurationMinutes, 30)
            .Add(m => m.DurationMinutesChanged, EventCallback.Factory.Create<int>(this, v => duracionRecibida = v)));

        cut.Find("#durationInput").Change("90");
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        Assert.Equal(90, duracionRecibida);
    }

    [Fact]
    public async Task Confirmar_envia_duracion_personalizada()
    {
        var duracionRecibida = 0;

        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, false)
            .Add(m => m.ShowDuration, true)
            .Add(m => m.DurationMinutes, 30)
            .Add(m => m.DurationMinutesChanged, EventCallback.Factory.Create<int>(this, v => duracionRecibida = v)));

        cut.Find("#durationInput").Change("120");
        await cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());

        Assert.Equal(120, duracionRecibida);
    }

    // ── Cancelar ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_invoca_OnCancel()
    {
        var cancelado = false;

        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.OnCancel, EventCallback.Factory.Create(this, () => cancelado = true)));

        await cut.Find("button.btn-secondary").ClickAsync(new MouseEventArgs());

        Assert.True(cancelado);
    }

    [Fact]
    public async Task Click_en_backdrop_invoca_OnCancel()
    {
        var cancelado = false;

        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.OnCancel, EventCallback.Factory.Create(this, () => cancelado = true)));

        await cut.Find(".modal-backdrop").ClickAsync(new MouseEventArgs());

        Assert.True(cancelado);
    }

    // ── Combinaciones de acción ──────────────────────────────────────────────

    [Theory]
    [InlineData(true,  false, true,  false)] // kick:  reason sí, duration no
    [InlineData(false, true,  false, true)]  // mute:  reason no, duration sí
    [InlineData(false, true,  false, true)]  // gag:   reason no, duration sí
    [InlineData(true,  true,  true,  true)]  // ban:   reason sí, duration sí
    public void Modal_muestra_campos_correctos_segun_accion(
        bool showReason, bool showDuration,
        bool expectReason, bool expectDuration)
    {
        var cut = Render<ReasonModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.ShowReason, showReason)
            .Add(m => m.ShowDuration, showDuration));

        Assert.Equal(expectReason,   cut.FindAll("#reasonInput").Count == 1);
        Assert.Equal(expectDuration, cut.FindAll("#durationInput").Count == 1);
    }
}
