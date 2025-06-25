using Analyst.Utils;
using ClientService.EF;
using ClientService.Models;
using ClientService.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net;
using System.Text;
using System;

namespace Analyst
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly ApplicationDbContext _context;
        private readonly EncryptionUtils _encryptionUtils;
        private readonly AnalystService _analystService;

        public Worker(ILogger<Worker> logger, ApplicationDbContext context, EncryptionUtils encryptionUtils)
        {
            _logger = logger;
            _context = context;
            _encryptionUtils = encryptionUtils;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {

                List<IntegratedUsers> users = _context.IntegratedUsers.ToList();

                foreach (var user in users)
                {
                    user.TimeFlag = DateTime.Now;
                    user.IsWorking = true;
                    _context.SaveChanges();

                    HalykCredential credentils = _context.HalykCredentials.Where(x => x.UserId == user.UserId).First();
                    if (credentils == null)
                    {
                        User us = _context.Users.Where(x => x.Id == user.UserId).First();
                        if (us == null) continue;

                        _logger.LogError("У пользователя:" + us.PhoneNumber + " нет данных маркета");
                        continue; //TODO В будущем добавить функцию уведомления в бд
                    }

                    string login = credentils.Login;
                    string password = _encryptionUtils.Decrypt(credentils.EncriptPassword, user.UserId);

                    List<UserSettings> userSettings = _context.UserSettings.Where(x => x.UserId == user.UserId).ToList();

                    foreach (var userSetting in userSettings)
                    {
                        if (userSetting.ActualPrice == 0) continue;
                        string simpleName = AnalystUtils.SimplifyUser(userSetting.ProductName);

                        int? minPrice = await _analystService.ParseMinPriceAsync(simpleName);
                        if (!minPrice.HasValue) continue;

                        if (minPrice > userSetting.ActualPrice) continue;
                        if (userSetting.MinPrice != 0 && minPrice <= userSetting.MinPrice) continue;
                        if ((user.MaxPersent * userSetting.MinPrice / 100) >= minPrice) continue;

                        var productPoints = _context.ProductPoints.Where(p => p.MerchantProductCode == userSetting.MerchantProductCode).First();
                        if (productPoints == null) continue;

                        productPoints.Price = Convert.ToDouble(minPrice); 


                        var payload = new
                        {
                            loanPeriod = 24,
                            merchantProductCode = userSetting.MerchantProductCode,
                            pointByCity = productPoints
                        };
                        string? token = await _analystService.LoginAsync(login, password);

                        if (string.IsNullOrEmpty(token))
                        {
                            _logger.LogWarning($"Ошибка логина для {u}");
                            return;
                        }

                        var request = new HttpRequestMessage(HttpMethod.Put, url)
                        {
                            Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
                        };
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                        var response = await client.SendAsync(request);

                        if (!response.IsSuccessStatusCode)
                        {
                            var body = await response.Content.ReadAsStringAsync();
                            _logger.LogWarning($"Ошибка PUT-запроса: статус {response.StatusCode}, ответ: {body}");
                        }
                        else
                        {
                            _logger.LogInformation($"Успешно обновлена цена для {merchantCode} → {newPrice} ₸");
                        }
                        await _dbContext.SaveChangesAsync();





                    }


                }

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
